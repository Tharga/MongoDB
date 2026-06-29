using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Communication.Client.Communication;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Monitor.Client;

/// <summary>
/// Hosted service that subscribes to MongoDB monitoring events
/// and forwards completed calls and collection info to a central server via Tharga.Communication.
/// </summary>
internal sealed class MonitorForwarder : IHostedService, IDisposable
{
    private const int MinQueueMetricIntervalMs = 100;
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private readonly IDatabaseMonitor _databaseMonitor;
    private readonly IQueueMonitor _queueMonitor;
    private readonly IConnectionPoolMonitor _connectionPoolMonitor;
    private readonly IClientCommunication _clientCommunication;
    private readonly LiveMonitoringState _liveMonitoringState;
    private readonly MonitorOptions _monitorOptions;
    private readonly MonitorRecordingState _recordingState;
    private readonly ILogger<MonitorForwarder> _logger;
    private readonly ConcurrentDictionary<Guid, CallStartEventArgs> _pendingCalls = new();
    private Timer _queueMetricTimer;
    private bool _forwardCompletedCalls;
    private readonly object _callForwardingLock = new();

    public MonitorForwarder(
        IMongoDbServiceFactory mongoDbServiceFactory,
        IDatabaseMonitor databaseMonitor,
        IQueueMonitor queueMonitor,
        IConnectionPoolMonitor connectionPoolMonitor,
        IClientCommunication clientCommunication,
        LiveMonitoringState liveMonitoringState,
        IOptions<DatabaseOptions> databaseOptions,
        MonitorRecordingState recordingState = null,
        ILogger<MonitorForwarder> logger = null)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _databaseMonitor = databaseMonitor;
        _queueMonitor = queueMonitor;
        _connectionPoolMonitor = connectionPoolMonitor;
        _clientCommunication = clientCommunication;
        _liveMonitoringState = liveMonitoringState;
        _monitorOptions = databaseOptions.Value.Monitor;
        _recordingState = recordingState;
        _logger = logger;
    }

    // The agent's calls are "consumed" while forwarding is on or a live viewer is attached. Drives
    // CallRecordingLevel gating (OnDemand step capture / WhenConsumed recording) on the agent.
    private void UpdateCallsConsumed()
    {
        if (_recordingState != null)
            _recordingState.CallsConsumed = _forwardCompletedCalls || _liveMonitoringState.Active;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _forwardCompletedCalls = _monitorOptions.ForwardCompletedCalls;
        UpdateCallsConsumed();

        // Completed-call forwarding is opt-in (it can be a large, continuous stream). Only subscribe to call
        // events when enabled — otherwise we don't even track pending calls.
        if (_forwardCompletedCalls)
        {
            _mongoDbServiceFactory.CallStartEvent += OnCallStart;
            _mongoDbServiceFactory.CallEndEvent += OnCallEnd;
        }

        _databaseMonitor.CollectionInfoChangedEvent += OnCollectionInfoChanged;
        _databaseMonitor.CollectionDroppedEvent += OnCollectionDropped;

        var intervalMs = Math.Max(MinQueueMetricIntervalMs, (int)_monitorOptions.QueueMetricInterval.TotalMilliseconds);
        _queueMetricTimer = new Timer(OnQueueMetricTick, null, intervalMs, intervalMs);

        // Report our config + send all known collections once connected.
        _ = SendInitialCollectionInfoAsync(cancellationToken);

        return Task.CompletedTask;
    }

    private async Task SendInitialCollectionInfoAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Wait for connection to establish
            for (var i = 0; i < 60 && !cancellationToken.IsCancellationRequested; i++)
            {
                if (_clientCommunication.IsConnected) break;
                await Task.Delay(1000, cancellationToken);
            }

            if (!_clientCommunication.IsConnected) return;

            // Report this agent's forwarding config so the central monitor can show it on the Clients page.
            await SendClientStatusAsync();

            await ResendCollectionInfoAsync(cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to send initial collection info.");
        }
    }

    /// <summary>
    /// Re-scans this agent's collections and forwards a fresh snapshot of each to the central server.
    /// Called on startup and again when the server requests a cache reset, so the server can rebuild
    /// its remote view from current data rather than waiting for the next incidental access.
    /// </summary>
    public async Task ResendCollectionInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_clientCommunication.IsConnected) return;

            // Collect fingerprints first, then refresh stats for each.
            // GetInstancesAsync uses includeDetails: false so stats are null.
            var collections = await _databaseMonitor.GetInstancesAsync().ToListAsync(cancellationToken);

            foreach (var info in collections)
            {
                try
                {
                    await _databaseMonitor.RefreshStatsAsync(info);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Failed to refresh stats for {Collection} during sync.", info.CollectionName);
                }
            }

            // Re-fetch after stats refresh — CollectionInfoChangedEvent will have
            // updated the cache, and the event handler sends each one individually.
            // But in case events didn't fire, send explicitly.
            foreach (var info in collections)
            {
                var refreshed = await _databaseMonitor.GetInstanceAsync(info);
                if (refreshed != null)
                {
                    var message = BuildCollectionInfoMessage(refreshed);
                    await ForwardCollectionInfoAsync(message);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to send collection info.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_forwardCompletedCalls)
        {
            _mongoDbServiceFactory.CallStartEvent -= OnCallStart;
            _mongoDbServiceFactory.CallEndEvent -= OnCallEnd;
        }
        _databaseMonitor.CollectionInfoChangedEvent -= OnCollectionInfoChanged;
        _databaseMonitor.CollectionDroppedEvent -= OnCollectionDropped;
        _queueMetricTimer?.Dispose();
        _pendingCalls.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Turns completed-call forwarding on or off at runtime (the server can drive this per agent).
    /// Subscribing to call events is what enables forwarding, so this attaches/detaches them, then
    /// re-reports the agent's status so the central monitor reflects the new state. Returns the
    /// resulting state.
    /// </summary>
    public async Task<bool> SetCallForwardingAsync(bool enabled)
    {
        lock (_callForwardingLock)
        {
            if (enabled != _forwardCompletedCalls)
            {
                if (enabled)
                {
                    _mongoDbServiceFactory.CallStartEvent += OnCallStart;
                    _mongoDbServiceFactory.CallEndEvent += OnCallEnd;
                }
                else
                {
                    _mongoDbServiceFactory.CallStartEvent -= OnCallStart;
                    _mongoDbServiceFactory.CallEndEvent -= OnCallEnd;
                    _pendingCalls.Clear();
                }
                _forwardCompletedCalls = enabled;
                UpdateCallsConsumed();
            }
        }

        await SendClientStatusAsync();
        return _forwardCompletedCalls;
    }

    private async Task SendClientStatusAsync()
    {
        try
        {
            if (!_clientCommunication.IsConnected) return;

            var intervalMs = Math.Max(MinQueueMetricIntervalMs, (int)_monitorOptions.QueueMetricInterval.TotalMilliseconds);
            await _clientCommunication.PostAsync(new MonitorClientStatusMessage
            {
                SourceName = _mongoDbServiceFactory.SourceName,
                ForwardCompletedCalls = _forwardCompletedCalls,
                QueueMetricIntervalMs = intervalMs,
                StorageMode = _monitorOptions.StorageMode.ToString(),
                EnableCommandMonitoring = _monitorOptions.EnableCommandMonitoring,
            });
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to send client status.");
        }
    }

    public void Dispose()
    {
        _queueMetricTimer?.Dispose();
        _pendingCalls.Clear();
    }

    private void OnCallStart(object sender, CallStartEventArgs e)
    {
        _pendingCalls[e.CallKey] = e;
    }

    private void OnCallEnd(object sender, CallEndEventArgs e)
    {
        if (!e.Final) return;

        if (!_pendingCalls.TryRemove(e.CallKey, out var start))
        {
            _logger?.LogDebug("Received CallEndEvent for unknown call {CallKey}, skipping.", e.CallKey);
            return;
        }

        var callDto = BuildCallDto(start, e);
        var message = new MonitorCallMessage { Call = callDto };

        _ = ForwardAsync(message);
    }

    private void OnCollectionInfoChanged(object sender, CollectionInfoChangedEventArgs e)
    {
        var message = BuildCollectionInfoMessage(e.CollectionInfo);
        _ = ForwardCollectionInfoAsync(message);
    }

    private void OnCollectionDropped(object sender, CollectionDroppedEventArgs e)
    {
        // Use the resolved identity carried on the event; fall back to the DatabaseContext. Without
        // a collection name there's nothing the server can match, so skip.
        var collectionName = e.CollectionName ?? e.DatabaseContext?.CollectionName;
        if (string.IsNullOrEmpty(collectionName)) return;

        var message = new MonitorCollectionDroppedMessage
        {
            SourceName = _mongoDbServiceFactory.SourceName,
            ConfigurationName = e.ConfigurationName ?? e.DatabaseContext?.ConfigurationName?.Value,
            DatabaseName = e.DatabaseName,
            CollectionName = collectionName,
        };
        _ = ForwardCollectionDroppedAsync(message);
    }

    private async Task ForwardAsync(MonitorCallMessage message)
    {
        try
        {
            if (!_clientCommunication.IsConnected) return;
            await _clientCommunication.PostAsync(message);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to forward monitoring data for call {CallKey}.", message.Call.Key);
        }
    }

    private async Task ForwardCollectionInfoAsync(MonitorCollectionInfoMessage message)
    {
        try
        {
            if (!_clientCommunication.IsConnected) return;
            await _clientCommunication.PostAsync(message);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to forward collection info for {Collection}.", message.CollectionName);
        }
    }

    private async Task ForwardCollectionDroppedAsync(MonitorCollectionDroppedMessage message)
    {
        try
        {
            if (!_clientCommunication.IsConnected) return;
            await _clientCommunication.PostAsync(message);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to forward collection drop for {Collection}.", message.CollectionName);
        }
    }

    private (bool Connected, bool HasSubscribers, bool HasPools)? _lastLiveState;

    // Logs only when the live-monitoring gating changes, so the console shows when a subscription
    // starts/ends and why queue metrics are (not) being sent — without spamming every tick.
    private void LogLiveStateChange(bool connected, bool hasSubscribers, bool hasPools)
    {
        var state = (connected, hasSubscribers, hasPools);
        if (_lastLiveState == state) return;
        _lastLiveState = state;

        if (!connected)
            _logger?.LogInformation("Live monitoring: not connected to the monitor server — not sending queue metrics.");
        else if (!hasSubscribers)
            _logger?.LogInformation("Live monitoring: no server subscriber (Queue view closed) — not sending queue metrics.");
        else if (!hasPools)
            _logger?.LogInformation("Live monitoring: subscription active, but no connection pool yet (access MongoDB first) — nothing to send.");
        else
            _logger?.LogInformation("Live monitoring: subscription active — sending queue metrics.");
    }

    private async void OnQueueMetricTick(object state)
    {
        try
        {
            // Keep the "calls consumed" signal current as the live-viewer state changes (≤ one interval lag).
            UpdateCallsConsumed();

            var connected = _clientCommunication.IsConnected;
            // Prefer the explicit server-pushed flag (SetLiveMonitoringActiveMessage). Fall back to the
            // framework's HasSubscribers as a secondary signal — the explicit flag is what makes this
            // reliable across deployments where the built-in subscription propagation doesn't reach agents.
            var hasSubscribers = connected
                && (_liveMonitoringState.Active || _clientCommunication.HasSubscribers<LiveMonitoringMarker>());
            if (!connected || !hasSubscribers)
            {
                LogLiveStateChange(connected, hasSubscribers, false);
                return;
            }

            // Merge the limiter's per-pool queue view with the actual connection counts, by server-key.
            var byServer = new Dictionary<string, PoolMetricDto>();
            foreach (var p in _queueMonitor.GetPerPoolState())
            {
                byServer[p.ServerKey] = new PoolMetricDto
                {
                    ServerKey = p.ServerKey,
                    ConfigurationNames = p.ConfigurationNames,
                    QueueCount = p.QueueCount,
                    ExecutingCount = p.ExecutingCount,
                    WaitTimeMs = p.LastWaitTimeMs > 0 ? p.LastWaitTimeMs : null,
                };
            }
            foreach (var c in _connectionPoolMonitor.GetSnapshot())
            {
                var baseDto = byServer.TryGetValue(c.ServerKey, out var existing)
                    ? existing
                    : new PoolMetricDto { ServerKey = c.ServerKey, ConfigurationNames = Array.Empty<string>(), QueueCount = 0, ExecutingCount = 0 };
                byServer[c.ServerKey] = baseDto with { OpenConnections = c.OpenConnections, MaxPoolSize = c.MaxPoolSize };
            }
            var pools = byServer.Values.ToList();
            LogLiveStateChange(connected, hasSubscribers, pools.Count > 0);

            // A live view is watching (gated above). Report whenever the agent has any pool to show —
            // even fully idle (all zeros) — so an idle agent still surfaces a line alongside the active
            // ones in the live Queue view. Only skip when there are no pools at all (the agent hasn't
            // touched MongoDB yet), since an empty report would produce no line anyway.
            if (pools.Count == 0) return;

            // Aggregate scalars (back-compat for older servers).
            var queueCount = pools.Sum(p => p.QueueCount);
            var executingCount = pools.Sum(p => p.ExecutingCount);
            var lastWaitTimeMs = pools.Max(p => p.WaitTimeMs ?? 0);

            await _clientCommunication.PostAsync(new MonitorQueueMetricMessage
            {
                SourceName = _mongoDbServiceFactory.SourceName,
                Timestamp = DateTime.UtcNow,
                QueueCount = queueCount,
                ExecutingCount = executingCount,
                WaitTimeMs = lastWaitTimeMs > 0 ? lastWaitTimeMs : null,
                Pools = pools,
            });
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to forward queue metric.");
        }
    }

    private MonitorCollectionInfoMessage BuildCollectionInfoMessage(CollectionInfo info)
    {
        return new MonitorCollectionInfoMessage
        {
            ConfigurationName = info.ConfigurationName.Value,
            DatabaseName = info.DatabaseName,
            CollectionName = info.CollectionName,
            SourceName = _mongoDbServiceFactory.SourceName,
            Server = info.Server,
            DatabasePart = info.DatabasePart,
            Discovery = info.Discovery.ToString(),
            Registration = info.Registration.ToString(),
            EntityTypes = info.EntityTypes,
            CollectionTypeName = info.CollectionType?.Name,
            Stats = info.Stats,
            Index = info.Index,
            Clean = info.Clean,
        };
    }

    private static CallDto BuildCallDto(CallStartEventArgs start, CallEndEventArgs end)
    {
        return new CallDto
        {
            Key = start.CallKey,
            StartTime = DateTime.UtcNow - end.Elapsed,
            SourceName = start.SourceName,
            ConfigurationName = start.Fingerprint.ConfigurationName.Value,
            DatabaseName = start.Fingerprint.DatabaseName,
            CollectionName = start.Fingerprint.CollectionName,
            FunctionName = start.FunctionName,
            Operation = start.Operation.ToString(),
            ElapsedMs = end.Elapsed.TotalMilliseconds,
            Count = end.Count,
            Exception = end.Exception?.Message,
            Final = end.Final,
            FilterJson = end.FilterJsonProvider?.Invoke(),
            Steps = end.Steps?.Select(s => new CallStepDto
            {
                Step = s.Step,
                DeltaMs = s.Delta.TotalMilliseconds,
                Message = s.Message
            }).ToArray()
        };
    }
}

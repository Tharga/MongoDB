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
    private readonly MonitorOptions _monitorOptions;
    private readonly ILogger<MonitorForwarder> _logger;
    private readonly ConcurrentDictionary<Guid, CallStartEventArgs> _pendingCalls = new();
    private Timer _queueMetricTimer;
    private bool _forwardCompletedCalls;

    public MonitorForwarder(
        IMongoDbServiceFactory mongoDbServiceFactory,
        IDatabaseMonitor databaseMonitor,
        IQueueMonitor queueMonitor,
        IConnectionPoolMonitor connectionPoolMonitor,
        IClientCommunication clientCommunication,
        IOptions<DatabaseOptions> databaseOptions,
        ILogger<MonitorForwarder> logger = null)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _databaseMonitor = databaseMonitor;
        _queueMonitor = queueMonitor;
        _connectionPoolMonitor = connectionPoolMonitor;
        _clientCommunication = clientCommunication;
        _monitorOptions = databaseOptions.Value.Monitor;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _forwardCompletedCalls = _monitorOptions.ForwardCompletedCalls;

        // Completed-call forwarding is opt-in (it can be a large, continuous stream). Only subscribe to call
        // events when enabled — otherwise we don't even track pending calls.
        if (_forwardCompletedCalls)
        {
            _mongoDbServiceFactory.CallStartEvent += OnCallStart;
            _mongoDbServiceFactory.CallEndEvent += OnCallEnd;
        }

        _databaseMonitor.CollectionInfoChangedEvent += OnCollectionInfoChanged;

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
                    _logger?.LogDebug(ex, "Failed to refresh stats for {Collection} during initial sync.", info.CollectionName);
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
            _logger?.LogDebug(ex, "Failed to send initial collection info.");
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
        _queueMetricTimer?.Dispose();
        _pendingCalls.Clear();
        return Task.CompletedTask;
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

    private async void OnQueueMetricTick(object state)
    {
        try
        {
            if (!_clientCommunication.IsConnected) return;
            if (!_clientCommunication.HasSubscribers<LiveMonitoringMarker>()) return;

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

            // Aggregate scalars (back-compat for older servers + the activity guard below).
            var queueCount = pools.Sum(p => p.QueueCount);
            var executingCount = pools.Sum(p => p.ExecutingCount);
            var lastWaitTimeMs = pools.Count == 0 ? 0d : pools.Max(p => p.WaitTimeMs ?? 0);
            var openConnections = pools.Sum(p => p.OpenConnections);

            // Send when there's queue activity OR open connections to report (so idle-but-open pools surface).
            if (queueCount == 0 && executingCount == 0 && lastWaitTimeMs == 0 && openConnections == 0) return;

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

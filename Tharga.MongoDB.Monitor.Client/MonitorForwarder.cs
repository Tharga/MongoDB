using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tharga.Communication.Client.Communication;

namespace Tharga.MongoDB.Monitor.Client;

/// <summary>
/// Hosted service that subscribes to MongoDB monitoring events
/// and forwards completed calls to a central server via Tharga.Communication.
/// </summary>
internal sealed class MonitorForwarder : IHostedService, IDisposable
{
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private readonly IClientCommunication _clientCommunication;
    private readonly ILogger<MonitorForwarder> _logger;
    private readonly ConcurrentDictionary<Guid, CallStartEventArgs> _pendingCalls = new();

    public MonitorForwarder(
        IMongoDbServiceFactory mongoDbServiceFactory,
        IClientCommunication clientCommunication,
        ILogger<MonitorForwarder> logger = null)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _clientCommunication = clientCommunication;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _mongoDbServiceFactory.CallStartEvent += OnCallStart;
        _mongoDbServiceFactory.CallEndEvent += OnCallEnd;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _mongoDbServiceFactory.CallStartEvent -= OnCallStart;
        _mongoDbServiceFactory.CallEndEvent -= OnCallEnd;
        _pendingCalls.Clear();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
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

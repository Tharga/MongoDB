using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Tharga.Communication.Contract;
using Tharga.Communication.Server;
using Tharga.Communication.Server.Communication;

namespace Tharga.MongoDB.Monitor.Server;

/// <summary>
/// Bridges client connection events from <see cref="MonitorClientStateService"/>
/// into <see cref="IDatabaseMonitor"/> so Blazor components can display connected agents.
/// </summary>
internal sealed class MonitorClientBridge : IHostedService
{
    private readonly MonitorClientStateService _clientStateService;
    private readonly IDatabaseMonitor _databaseMonitor;
    private readonly IServerCommunication _serverCommunication;

    public MonitorClientBridge(MonitorClientStateService clientStateService, IDatabaseMonitor databaseMonitor, IServerCommunication serverCommunication)
    {
        _clientStateService = clientStateService;
        _databaseMonitor = databaseMonitor;
        _serverCommunication = serverCommunication;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _clientStateService.ConnectionChangedEvent += OnConnectionChanged;
        _clientStateService.DisconnectedEvent += OnDisconnected;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _clientStateService.ConnectionChangedEvent -= OnConnectionChanged;
        _clientStateService.DisconnectedEvent -= OnDisconnected;
        return Task.CompletedTask;
    }

    private void OnConnectionChanged(object sender, ConnectionChangedEventArgs e)
    {
        if (e.ClientConnectionInfo is MonitorClientConnectionInfo info)
        {
            _databaseMonitor.IngestClientConnected(new MonitorClientDto
            {
                Instance = info.Instance,
                ConnectionId = info.ConnectionId,
                Machine = info.Machine,
                Type = info.Type,
                Version = info.Version,
                IsConnected = true,
                ConnectTime = info.ConnectTime,
                AuthKeyName = info.KeyName,
            });

            // Replay current subscription state to the freshly-connected agent. Tharga.Communication
            // only pushes SubscriptionStateChanged on the 0<->1 boundary, so an agent that connects
            // (or reconnects) while a subscription is already active would otherwise never learn of
            // it — and would never start sending live data (e.g. queue metrics gated on
            // HasSubscribers<LiveMonitoringMarker>).
            _ = ReplaySubscriptionsAsync(info.ConnectionId);
        }
    }

    private async Task ReplaySubscriptionsAsync(string connectionId)
    {
        try
        {
            foreach (var (topic, count) in _serverCommunication.GetSubscriptions())
            {
                if (count <= 0) continue;
                await _serverCommunication.PostAsync(connectionId, new SubscriptionStateChanged
                {
                    Topic = topic,
                    Key = null,
                    HasSubscribers = true,
                });
            }
        }
        catch
        {
            // Best-effort: a transient post failure on connect must not disrupt connection handling.
        }
    }

    private void OnDisconnected(object sender, DisconnectedEventArgs e)
    {
        if (e.Item is MonitorClientConnectionInfo info)
        {
            _databaseMonitor.IngestClientDisconnected(info.ConnectionId);
        }
    }
}

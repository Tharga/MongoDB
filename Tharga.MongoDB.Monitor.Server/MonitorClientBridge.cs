using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
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

    public MonitorClientBridge(MonitorClientStateService clientStateService, IDatabaseMonitor databaseMonitor)
    {
        _clientStateService = clientStateService;
        _databaseMonitor = databaseMonitor;
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
            });
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

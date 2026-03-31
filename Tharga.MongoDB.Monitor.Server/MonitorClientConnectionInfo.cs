using System;
using Tharga.Communication.Server;

namespace Tharga.MongoDB.Monitor.Server;

/// <summary>
/// Connection info for a remote monitoring agent.
/// </summary>
public record MonitorClientConnectionInfo : IClientConnectionInfo
{
    public Guid Instance { get; init; }
    public string ConnectionId { get; init; }
    public string Machine { get; init; }
    public string Type { get; init; }
    public string Version { get; init; }
    public bool IsConnected { get; init; }
    public DateTime ConnectTime { get; init; }
    public DateTime? DisconnectTime { get; init; }
}

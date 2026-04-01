using System;

namespace Tharga.MongoDB;

/// <summary>
/// Serialization-friendly representation of a connected monitoring agent.
/// </summary>
public record MonitorClientDto
{
    public required Guid Instance { get; init; }
    public required string ConnectionId { get; init; }
    public required string Machine { get; init; }
    public required string Type { get; init; }
    public required string Version { get; init; }
    public required bool IsConnected { get; init; }
    public required DateTime ConnectTime { get; init; }
    public DateTime? DisconnectTime { get; init; }
    public string SourceName { get; init; }
}

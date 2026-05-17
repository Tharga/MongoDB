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

    /// <summary>
    /// Human-readable name of the API key used to authenticate this connection,
    /// as reported by the registered <c>IApiKeyValidator</c>. <c>null</c> when the
    /// connection was accepted without a key, or when the validator did not
    /// supply a name.
    /// </summary>
    public string AuthKeyName { get; init; }
}

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

    /// <summary>
    /// The agent's reported monitor configuration (call forwarding, queue interval, …). <c>null</c> until the
    /// agent reports it, or for agents on a version that predates status reporting.
    /// </summary>
    public MonitorClientStatus Status { get; init; }
}

/// <summary>
/// The forwarding-related configuration an agent reports about itself, surfaced on the Clients page.
/// </summary>
public record MonitorClientStatus
{
    /// <summary>Whether the agent forwards every completed call (off by default — can be a large stream).</summary>
    public required bool ForwardCompletedCalls { get; init; }

    /// <summary>How often the agent forwards a queue-state snapshot, in milliseconds.</summary>
    public required int QueueMetricIntervalMs { get; init; }

    /// <summary>Where the agent persists its monitor state (e.g. Database / Memory).</summary>
    public string StorageMode { get; init; }

    /// <summary>Whether the agent captures MongoDB driver command durations.</summary>
    public bool EnableCommandMonitoring { get; init; }
}

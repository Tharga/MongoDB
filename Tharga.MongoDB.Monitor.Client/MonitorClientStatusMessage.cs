namespace Tharga.MongoDB.Monitor.Client;

/// <summary>
/// Sent once when an agent connects, reporting its forwarding-related configuration so the central
/// monitor can show it on the Clients page. Correlated to the agent by <see cref="SourceName"/>.
/// </summary>
public record MonitorClientStatusMessage
{
    public required string SourceName { get; init; }
    public required bool ForwardCompletedCalls { get; init; }
    public required int QueueMetricIntervalMs { get; init; }
    public string StorageMode { get; init; }
    public bool EnableCommandMonitoring { get; init; }

    /// <summary>The agent's <c>Tharga.MongoDB.Monitor.Client</c> library version, for display on the Clients page.</summary>
    public string LibraryVersion { get; init; }
}

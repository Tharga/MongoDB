using System;
using System.Collections.Generic;

namespace Tharga.MongoDB.Monitor.Client;

/// <summary>
/// Message sent from a remote agent to the central monitor server
/// containing a queue state snapshot.
/// </summary>
public record MonitorQueueMetricMessage
{
    public required string SourceName { get; init; }
    public required DateTime Timestamp { get; init; }

    // Aggregate (process-wide) scalars. Retained for backward/forward compatibility: an older server
    // ignores Pools and reads these; an older agent sends only these and Pools stays null.
    public required int QueueCount { get; init; }
    public required int ExecutingCount { get; init; }
    public double? WaitTimeMs { get; init; }

    /// <summary>Per-connection-pool breakdown. When present, the server prefers this over the aggregate scalars.</summary>
    public IReadOnlyList<PoolMetricDto> Pools { get; init; }
}

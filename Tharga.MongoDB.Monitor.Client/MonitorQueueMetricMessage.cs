using System;

namespace Tharga.MongoDB.Monitor.Client;

/// <summary>
/// Message sent from a remote agent to the central monitor server
/// containing a queue state snapshot.
/// </summary>
public record MonitorQueueMetricMessage
{
    public required string SourceName { get; init; }
    public required DateTime Timestamp { get; init; }
    public required int QueueCount { get; init; }
    public required int ExecutingCount { get; init; }
    public double? WaitTimeMs { get; init; }
}

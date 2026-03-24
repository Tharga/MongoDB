using System;

namespace Tharga.MongoDB;

public record QueueMetricEventArgs
{
    public required DateTime Timestamp { get; init; }
    public required int QueueCount { get; init; }
    public required int ExecutingCount { get; init; }
    public TimeSpan? WaitTime { get; init; }
}

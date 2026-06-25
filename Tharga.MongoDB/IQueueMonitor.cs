using System;
using System.Collections.Generic;

namespace Tharga.MongoDB;

public interface IQueueMonitor
{
    event EventHandler<QueueMetricEventArgs> QueueMetricEvent;
    IReadOnlyList<QueueMetricEventArgs> GetRecentMetrics();
    (int QueueCount, int ExecutingCount, double LastWaitTimeMs) GetCurrentState();

    /// <summary>
    /// Per-connection-pool queue state. Each entry is one physical pool (keyed by <see cref="PoolQueueState.ServerKey"/>),
    /// carrying the configuration name(s) that have used it. Reading resets the per-pool wait-time high-water mark.
    /// </summary>
    IReadOnlyList<PoolQueueState> GetPerPoolState();
}

/// <summary>
/// A snapshot of one connection pool's queue state. The pool is the real unit of concurrency contention
/// (one <c>MongoClient</c>/pool per server-key); <see cref="ConfigurationNames"/> are the named configurations
/// that route through it.
/// </summary>
public record PoolQueueState
{
    public required string ServerKey { get; init; }
    public required IReadOnlyCollection<string> ConfigurationNames { get; init; }
    public required int QueueCount { get; init; }
    public required int ExecutingCount { get; init; }
    public required double LastWaitTimeMs { get; init; }
}

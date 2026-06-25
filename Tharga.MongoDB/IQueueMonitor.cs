using System;
using System.Collections.Generic;
using Tharga.MongoDB.Disk;

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

    /// <summary>
    /// The calls currently in the limiter — both queued (waiting for a pool slot) and executing. Intended for
    /// on-demand diagnostics (e.g. via MCP): the query filter, if any, is rendered while building this list, not
    /// on the execution path. Empty when the limiter is disabled.
    /// </summary>
    IReadOnlyList<InFlightCallInfo> GetInFlightCalls();
}

/// <summary>
/// A single call currently held by the limiter — either queued (waiting for a connection-pool slot) or executing.
/// </summary>
public record InFlightCallInfo
{
    public required Guid CallKey { get; init; }
    public required string ServerKey { get; init; }
    public string ConfigurationName { get; init; }
    public string DatabaseName { get; init; }
    public string CollectionName { get; init; }
    public string FunctionName { get; init; }
    public Operation Operation { get; init; }

    /// <summary>True once the call has acquired a pool slot and is running; false while still queued.</summary>
    public required bool IsExecuting { get; init; }

    /// <summary>When the call entered the limiter (UTC).</summary>
    public required DateTime EnqueuedUtc { get; init; }

    /// <summary>The rendered query filter, if one was supplied and could be rendered; otherwise null.</summary>
    public string Filter { get; init; }
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

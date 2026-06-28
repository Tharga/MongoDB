using System;
using System.Collections.Generic;

namespace Tharga.MongoDB;

/// <summary>
/// Serialization-friendly representation of a database call.
/// </summary>
public record CallDto
{
    public required Guid Key { get; init; }
    public required DateTime StartTime { get; init; }
    public required string SourceName { get; init; }
    public required string ConfigurationName { get; init; }
    public required string DatabaseName { get; init; }
    public required string CollectionName { get; init; }
    public required string FunctionName { get; init; }
    public required string Operation { get; init; }
    public double? ElapsedMs { get; init; }
    public int? Count { get; init; }
    public string Exception { get; init; }
    public bool Final { get; init; }
    public string FilterJson { get; init; }
    public IReadOnlyList<CallStepDto> Steps { get; init; }
}

/// <summary>
/// Serialization-friendly representation of a call execution step.
/// </summary>
public record CallStepDto
{
    public required string Step { get; init; }
    public required double DeltaMs { get; init; }
    public string Message { get; init; }
}

/// <summary>
/// Summary of calls grouped by collection and function.
/// </summary>
public record CallSummaryDto
{
    public required string SourceName { get; init; }
    public required string ConfigurationName { get; init; }
    public required string DatabaseName { get; init; }
    public required string CollectionName { get; init; }
    public required string FunctionName { get; init; }
    public required int CallCount { get; init; }
    public required double AvgElapsedMs { get; init; }
    public required double MaxElapsedMs { get; init; }
    public required double MinElapsedMs { get; init; }
    public required double TotalElapsedMs { get; init; }
}

/// <summary>
/// Summary of errors grouped by exception type and collection.
/// </summary>
public record ErrorSummaryDto
{
    public required string SourceName { get; init; }
    public required string ConfigurationName { get; init; }
    public required string DatabaseName { get; init; }
    public required string CollectionName { get; init; }
    public required string ExceptionType { get; init; }
    public required string Message { get; init; }
    public required int Count { get; init; }
    public required DateTime LastOccurrence { get; init; }
}

/// <summary>
/// Slow call that may lack index coverage.
/// </summary>
public record SlowCallWithIndexInfoDto
{
    public required CallDto Call { get; init; }
    public required string[] DefinedIndexNames { get; init; }
    public required bool HasPotentialIndexCoverage { get; init; }
}

/// <summary>
/// Connection pool state. When produced per pool (see <see cref="IDatabaseMonitor.GetPerPoolQueueState"/>)
/// <see cref="Label"/> / <see cref="ConfigurationNames"/> describe which configuration(s) route through the pool;
/// for the aggregate process-wide state they are left null.
/// </summary>
public record ConnectionPoolStateDto
{
    public required int QueueCount { get; init; }
    public required int ExecutingCount { get; init; }
    public required double LastWaitTimeMs { get; init; }
    public required IReadOnlyList<QueueMetricDto> RecentMetrics { get; init; }

    /// <summary>Display label for the pool — the configuration name(s) routing through it (source-suffixed when ambiguous).</summary>
    public string Label { get; init; }

    /// <summary>The configuration name(s) that have used this pool.</summary>
    public IReadOnlyCollection<string> ConfigurationNames { get; init; }
}

/// <summary>
/// Per-pool queue snapshot used both for remote-agent ingest and over the wire
/// (<c>MonitorQueueMetricMessage.Pools</c>).
/// </summary>
public record PoolMetricDto
{
    public required string ServerKey { get; init; }
    public required IReadOnlyCollection<string> ConfigurationNames { get; init; }
    public required int QueueCount { get; init; }
    public required int ExecutingCount { get; init; }
    public double? WaitTimeMs { get; init; }

    /// <summary>Actual open driver connections for this pool (counts toward the cluster connection limit).</summary>
    public int OpenConnections { get; init; }

    /// <summary>Configured max pool size for this pool (capacity ceiling).</summary>
    public int MaxPoolSize { get; init; }
}

/// <summary>
/// Aggregated connection usage for one <b>cluster</b> (the server host(s) — the thing you connect <i>to</i>)
/// across all reporting sources (the central server plus every connected agent). This is the top of a
/// three-level hierarchy that keeps the dimensions distinct:
/// <list type="bullet">
/// <item><b>Cluster</b> (this record) — one MongoDB deployment, identified by its host(s). The summed
/// <see cref="OpenConnections"/> is what counts against a server-side limit (e.g. an Atlas tier's max).</item>
/// <item><b>Pool</b> (<see cref="ClusterPoolSummary"/>) — a distinct client connection pool to that cluster.
/// Two configurations to the same cluster share one pool unless they differ in max pool size, in which case
/// the cluster carries more than one pool.</item>
/// <item><b>Source</b> (<see cref="ClusterPoolSourceConnections"/>) — one process's pool (this server or a
/// single agent); the unit you'd point at to say "that one client/server item".</item>
/// </list>
/// </summary>
public record ClusterConnectionSummary
{
    /// <summary>The cluster identity — the server host(s) connected to, e.g. <c>localhost:27017</c>. Shared by every pool below.</summary>
    public required string Cluster { get; init; }

    /// <summary>Every configuration name that routes through this cluster (union across its pools).</summary>
    public required IReadOnlyCollection<string> ConfigurationNames { get; init; }

    /// <summary>Number of distinct sources (processes) contributing connections to this cluster.</summary>
    public required int SourceCount { get; init; }

    /// <summary>Total actual open connections across every pool and source — the figure that counts against <see cref="Limit"/>.</summary>
    public required int OpenConnections { get; init; }

    /// <summary>Total capacity (sum of each source-pool's configured max pool size) — the most connections this fleet could open.</summary>
    public required int MaxConnections { get; init; }

    /// <summary>Configured connection limit for the cluster (e.g. Atlas max), or null when not configured.</summary>
    public int? Limit { get; init; }

    /// <summary>The distinct connection pools to this cluster (one per server-key). Usually one; more than one when configurations differ in max pool size.</summary>
    public required IReadOnlyList<ClusterPoolSummary> Pools { get; init; }
}

/// <summary>
/// One connection pool within a <see cref="ClusterConnectionSummary"/> — a distinct server-key (cluster host(s)
/// + max pool size) — aggregated across every source that holds such a pool.
/// </summary>
public record ClusterPoolSummary
{
    public required string ServerKey { get; init; }

    /// <summary>The configured max pool size for this pool (the per-pool, per-source connection ceiling, e.g. 100).</summary>
    public required int MaxPoolSize { get; init; }

    /// <summary>Configuration name(s) that route through this pool.</summary>
    public required IReadOnlyCollection<string> ConfigurationNames { get; init; }

    /// <summary>Number of distinct sources (processes) that hold this pool.</summary>
    public required int SourceCount { get; init; }

    /// <summary>Total open connections in this pool across all sources.</summary>
    public required int OpenConnections { get; init; }

    /// <summary>Calls waiting for a slot in this pool's limiter, summed across sources. The queue is per pool — this is its real home.</summary>
    public required int QueueCount { get; init; }

    /// <summary>Calls currently executing through this pool's limiter, summed across sources.</summary>
    public required int ExecutingCount { get; init; }

    /// <summary>Per-source breakdown — one entry per process holding this pool.</summary>
    public required IReadOnlyList<ClusterPoolSourceConnections> Sources { get; init; }
}

/// <summary>
/// One process's contribution to a pool — the most granular "single client/server item": how many connections
/// this one source currently has open and the cap it can reach.
/// </summary>
public record ClusterPoolSourceConnections
{
    /// <summary>The reporting source (process), e.g. <c>MACHINE/App</c>.</summary>
    public required string Source { get; init; }

    /// <summary>Open connections this source currently holds in the pool.</summary>
    public required int OpenConnections { get; init; }

    /// <summary>This source's configured max pool size (the per-process ceiling).</summary>
    public required int MaxPoolSize { get; init; }

    /// <summary>Calls this source currently has waiting for a slot in the pool's limiter.</summary>
    public required int QueueCount { get; init; }

    /// <summary>Calls this source currently has executing through the pool's limiter.</summary>
    public required int ExecutingCount { get; init; }
}

/// <summary>
/// Serialization-friendly queue metric.
/// </summary>
public record QueueMetricDto
{
    public required DateTime Timestamp { get; init; }
    public required int QueueCount { get; init; }
    public required int ExecutingCount { get; init; }
    public double? WaitTimeMs { get; init; }
}

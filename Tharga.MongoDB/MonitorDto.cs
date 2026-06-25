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

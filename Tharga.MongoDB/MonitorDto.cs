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
/// Aggregate connection pool state.
/// </summary>
public record ConnectionPoolStateDto
{
    public required int QueueCount { get; init; }
    public required int ExecutingCount { get; init; }
    public required double LastWaitTimeMs { get; init; }
    public required IReadOnlyList<QueueMetricDto> RecentMetrics { get; init; }
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

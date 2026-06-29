using System;

namespace Tharga.MongoDB.Monitor.Client;

/// <summary>
/// Request to touch a collection on a remote agent.
/// </summary>
public record TouchCollectionRequest
{
    public required string ConfigurationName { get; init; }
    public required string DatabaseName { get; init; }
    public required string CollectionName { get; init; }
}

/// <summary>
/// Response from a touch action on a remote agent.
/// </summary>
public record TouchCollectionResponse
{
    public bool Success { get; init; }
    public string Error { get; init; }
}

/// <summary>
/// Request to drop indexes on a collection on a remote agent.
/// </summary>
public record DropIndexRequest
{
    public required string ConfigurationName { get; init; }
    public required string DatabaseName { get; init; }
    public required string CollectionName { get; init; }
}

/// <summary>
/// Response from a drop index action on a remote agent.
/// </summary>
public record DropIndexResponse
{
    public bool Success { get; init; }
    public int Before { get; init; }
    public int After { get; init; }
    public string Error { get; init; }
}

/// <summary>
/// Request to restore indexes on a collection on a remote agent.
/// </summary>
public record RestoreIndexRequest
{
    public required string ConfigurationName { get; init; }
    public required string DatabaseName { get; init; }
    public required string CollectionName { get; init; }
    public bool Force { get; init; }
}

/// <summary>
/// Response from a restore index action on a remote agent.
/// </summary>
public record RestoreIndexResponse
{
    public bool Success { get; init; }
    public string Error { get; init; }
}

/// <summary>
/// Request to clean a collection on a remote agent.
/// </summary>
public record CleanCollectionRequest
{
    public required string ConfigurationName { get; init; }
    public required string DatabaseName { get; init; }
    public required string CollectionName { get; init; }
    public bool CleanGuids { get; init; }
}

/// <summary>
/// Response from a clean action on a remote agent.
/// </summary>
public record CleanCollectionResponse
{
    public bool Success { get; init; }
    public CleanInfo CleanInfo { get; init; }
    public string Error { get; init; }
}

/// <summary>
/// Request to find duplicate documents by index on a remote agent.
/// </summary>
public record GetIndexBlockersRequest
{
    public required string ConfigurationName { get; init; }
    public required string DatabaseName { get; init; }
    public required string CollectionName { get; init; }
    public required string IndexName { get; init; }
}

/// <summary>
/// Response from a find duplicates action on a remote agent.
/// </summary>
public record GetIndexBlockersResponse
{
    public bool Success { get; init; }
    public string[][] Blockers { get; init; }
    public string Error { get; init; }
}

/// <summary>
/// Request to get an explain plan for a call on a remote agent.
/// </summary>
public record ExplainRequest
{
    public required Guid CallKey { get; init; }
}

/// <summary>
/// Response with explain JSON from a remote agent.
/// </summary>
public record ExplainResponse
{
    public bool Success { get; init; }
    public string ExplainJson { get; init; }
    public string Error { get; init; }
}

/// <summary>
/// Request to reset collection cache on a remote agent (fire-and-forget broadcast).
/// </summary>
public record ResetCacheRequest;

/// <summary>
/// Request to clear call history on a remote agent (fire-and-forget broadcast).
/// </summary>
public record ClearCallHistoryRequest;

/// <summary>
/// Request to turn completed-call forwarding on or off on a remote agent.
/// </summary>
public record SetCallForwardingRequest
{
    public bool Enabled { get; init; }
}

/// <summary>
/// Response from a set-call-forwarding action, carrying the agent's resulting state.
/// </summary>
public record SetCallForwardingResponse
{
    public bool Success { get; init; }
    public bool ForwardCompletedCalls { get; init; }
    public string Error { get; init; }
}

/// <summary>Request to turn command monitoring on or off on an agent.</summary>
public record SetCommandMonitoringRequest
{
    public bool Enabled { get; init; }
}

/// <summary>Response from a set-command-monitoring action, carrying the agent's resulting state.</summary>
public record SetCommandMonitoringResponse
{
    public bool Success { get; init; }
    public bool EnableCommandMonitoring { get; init; }
    public string Error { get; init; }
}

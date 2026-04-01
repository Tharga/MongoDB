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

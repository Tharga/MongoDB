namespace Tharga.MongoDB;

/// <summary>
/// Serialization-friendly representation of collection metadata from a remote agent.
/// </summary>
public record RemoteCollectionInfoDto
{
    public required string ConfigurationName { get; init; }
    public required string DatabaseName { get; init; }
    public required string CollectionName { get; init; }
    public required string SourceName { get; init; }
    public required string Server { get; init; }
    public string DatabasePart { get; init; }
    public string Discovery { get; init; }
    public string Registration { get; init; }
    public string[] EntityTypes { get; init; }
    public CollectionStats Stats { get; init; }
    public IndexInfo Index { get; init; }
    public CleanInfo Clean { get; init; }
}

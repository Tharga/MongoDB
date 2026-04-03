namespace Tharga.MongoDB.Monitor.Client;

/// <summary>
/// Message sent from a remote agent to the central monitor server
/// when collection metadata changes.
/// </summary>
public record MonitorCollectionInfoMessage
{
    public required string ConfigurationName { get; init; }
    public required string DatabaseName { get; init; }
    public required string CollectionName { get; init; }
    public required string SourceName { get; init; }
    public required string Server { get; init; }
    public string DatabasePart { get; init; }
    public required string Discovery { get; init; }
    public required string Registration { get; init; }
    public string[] EntityTypes { get; init; }
    public CollectionStats Stats { get; init; }
    public IndexInfo Index { get; init; }
    public CleanInfo Clean { get; init; }
}

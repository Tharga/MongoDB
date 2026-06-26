namespace Tharga.MongoDB.Monitor.Client;

/// <summary>
/// Message sent from a remote agent to the central monitor server when a collection is dropped,
/// so the server stops reporting it (and removes this agent as a source for it). Carries the
/// resolved identity the collection was reported with so the server can match it precisely.
/// </summary>
public record MonitorCollectionDroppedMessage
{
    public required string SourceName { get; init; }
    public required string ConfigurationName { get; init; }
    public required string DatabaseName { get; init; }
    public required string CollectionName { get; init; }
}

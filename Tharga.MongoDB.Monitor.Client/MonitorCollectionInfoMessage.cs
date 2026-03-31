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
}

namespace Tharga.MongoDB;

public record CollectionMeta
{
    //public required DatabaseContext DatabaseContext { get; init; } //TODO: replace with configuration name?
    public required string ConfigurationName { get; init; }
    public required string Server { get; init; }
    public required string DatabaseName { get; init; }
    public required string CollectionName { get; init; }
    public required long DocumentCount { get; init; }
    public required long Size { get; init; }
    public required string[] Types { get; init; }
    public required IndexMeta[] Indexes { get; init; }
}
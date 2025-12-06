namespace Tharga.MongoDB;

public record CollectionMeta : CollectionFingerprint
{
    public required string Server { get; init; }
    public required long DocumentCount { get; init; }
    public required long Size { get; init; }
    public required string[] Types { get; init; }
    public required IndexMeta[] Indexes { get; init; }
}
namespace Tharga.MongoDB;

public record CollectionInfo
{
    public required Source Source { get; init; }
    public required string ConfigurationName { get; init; }
    public required string Server { get; init; }
    public required string DatabaseName { get; init; }
    public required string CollectionName { get; init; }
    public string CollectionTypeName { get; init; }
    public Registration Registration { get; init; }
    public int AccessCount { get; init; }
    public required long DocumentCount { get; init; }
    public required long Size { get; init; }
    public required string[] Types { get; init; }
    //public required IndexMeta[] Indexes { get; init; }
}
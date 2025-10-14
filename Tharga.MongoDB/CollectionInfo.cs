namespace Tharga.MongoDB;

public record CollectionInfo
{
    public required Source Source { get; init; }
    public required string Name { get; init; }
    public required string[] TypeNames { get; init; }
    public string CollectionTypeName { get; init; }
    public Registration Registration { get; init; }
    public int AccessCount { get; init; }
}
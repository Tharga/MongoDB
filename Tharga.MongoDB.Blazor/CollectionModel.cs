namespace Tharga.MongoDB.Blazor;

public record CollectionModel : CollectionFingerprint
{
    public bool ValidDocumentCount => DocumentCount.IsValid;
    public required DocumentCount DocumentCount { get; init; }
    public required Source Source { get; init; }
    public required Registration Registration { get; init; }
    public bool Accessed => AccessCount > 0;
    public required int AccessCount { get; init; }
    public required long Size { get; init; }
    public required IndexModel[] Indices { get; init; }
    public required bool IndexEqualFields { get; init; }
}
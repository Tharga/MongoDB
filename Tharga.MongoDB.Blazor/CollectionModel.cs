namespace Tharga.MongoDB.Blazor;

public record CollectionModel : CollectionFingerprint
{
    public bool ValidDocumentCount => DocumentCount.IsValid;
    public required DocumentCount DocumentCount { get; set; }
    public Source Source { get; set; }
    public required Registration Registration { get; init; }
    public bool Accessed => AccessCount > 0;
    public required int AccessCount { get; set; }
    public required long Size { get; set; }
    public required IndexModel[] Indices { get; set; }
    public required bool IndexEqualFields { get; set; }
}
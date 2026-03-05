namespace Tharga.MongoDB.Blazor;

public record CollectionModel : CollectionFingerprint
{
    public CollectionStats Stats { get; set; }
    public Source Source { get; set; }
    public required Registration Registration { get; init; }
    public required long Size { get; set; }
    public required IndexModel[] Indices { get; set; }
    public required bool? IndexEqualFields { get; set; }
    public CleanInfo Clean { get; set; }
}

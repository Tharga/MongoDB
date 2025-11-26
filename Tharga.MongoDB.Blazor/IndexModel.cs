namespace Tharga.MongoDB.Blazor;

public record IndexModel
{
    public required string Name { get; init; }
    public required IndexMeta Current { get; init; }
    public required IndexMeta Defined { get; init; }
    public required bool EqualFields { get; init; }
}
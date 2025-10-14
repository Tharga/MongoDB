namespace Tharga.MongoDB;

public record IndexMeta
{
    public required string Name { get; init; }
    public required string[] Fields { get; init; }
    public required bool IsUnique { get; init; }
}
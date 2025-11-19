namespace Tharga.MongoDB;

public record IndexInfo
{
    public required IndexMeta[] Current { get; init; }
    public required IndexMeta[] Defined { get; init; }
}
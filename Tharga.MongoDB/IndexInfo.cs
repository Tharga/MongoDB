namespace Tharga.MongoDB;

public record IndexInfo
{
    public IndexMeta[] Current { get; init; }
    public required IndexMeta[] Defined { get; init; }
}
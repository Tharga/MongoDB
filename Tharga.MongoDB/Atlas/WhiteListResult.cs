namespace Tharga.MongoDB.Atlas;

internal record WhiteListResult
{
    public WhiteListItem[] Results { get; init; }
}
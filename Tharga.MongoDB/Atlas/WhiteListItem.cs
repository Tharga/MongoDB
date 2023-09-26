namespace Tharga.MongoDB.Atlas;

public record WhiteListItem
{
    public string IpAddress { get; init; }
    public string CidrBlock { get; init; }
    public string Comment { get; init; }
    public string GroupId { get; init; }
}
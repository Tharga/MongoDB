namespace Tharga.MongoDB;

public record DatabaseInfo
{
    internal DatabaseInfo()
    {
    }

    public bool CanConnect { get; init; }
    public int CollectionCount { get; init; }
    public string Message { get; init; }
    public string Firewall { get; init; }
}
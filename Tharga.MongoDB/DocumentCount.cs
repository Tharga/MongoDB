namespace Tharga.MongoDB;

public record DocumentCount
{
    public required long Count { get; init; }
    public long? Virtual { get; init; }

    public bool IsValid => Virtual == null || Virtual == Count;

    public static implicit operator long(DocumentCount item)
    {
        return item.Count;
    }
}
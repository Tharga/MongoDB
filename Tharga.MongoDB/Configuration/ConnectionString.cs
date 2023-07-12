namespace Tharga.MongoDB.Configuration;

public record ConnectionString
{
    private ConnectionString(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static implicit operator string(ConnectionString item)
    {
        return item?.Value;
    }

    public static implicit operator ConnectionString(string item)
    {
        return new ConnectionString(item);
    }
}
namespace Tharga.MongoDB.Configuration;

public record ConfigurationName
{
    private ConfigurationName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static implicit operator string(ConfigurationName item)
    {
        return item?.Value;
    }

    public static implicit operator ConfigurationName(string item)
    {
        return new ConfigurationName(item);
    }

    public override string ToString()
    {
        return Value;
    }
}
namespace Tharga.MongoDB.Configuration;

public record DatabaseUsage
{
    public string[] FirewallConfigurationNames { get; init; }
}
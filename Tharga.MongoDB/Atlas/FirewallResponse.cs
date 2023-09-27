using System.Net;

namespace Tharga.MongoDB.Atlas;

public record FirewallResponse
{
    public IPAddress IpAddress { get; init; }
    public string Name { get; init; }
    public EFirewallOpenResult Result { get; set; }
}

public enum EFirewallOpenResult { NoAccessProvided, AlreadyOpen, Open }
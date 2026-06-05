using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Quilt4Net.Toolkit.Features.Atlas;
using Quilt4Net.Toolkit.Features.ValueGroup;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

/// <summary>
/// Thin wrapper around <see cref="IAtlasFirewallClient"/> that builds a per-access
/// <see cref="AtlasFirewallProxyKeyEntry"/> from <see cref="MongoDbApiAccess"/>'s
/// Quilt4Net fields. Quilt4Net's factory binds one client to one entry, so callers
/// hand us the access record and we construct the entry per call.
/// </summary>
internal sealed class Quilt4NetFirewallService
{
    private readonly IAtlasFirewallClientFactory _factory;

    public Quilt4NetFirewallService(IAtlasFirewallClientFactory factory)
    {
        _factory = factory;
    }

    public Task<FirewallOpenResult> OpenAsync(MongoDbApiAccess access, IPAddress ip, CancellationToken cancellationToken = default)
    {
        var client = _factory.Create(BuildEntry(access, canManage: true));
        return client.OpenAsync(ip.ToString(), null, cancellationToken);
    }

    public Task<FirewallUsageResult> ReportUsedAsync(MongoDbApiAccess access, IPAddress ip, CancellationToken cancellationToken = default)
    {
        // Usage-only path: the key may be either a manage or usage key.
        var client = _factory.Create(BuildEntry(access, canManage: false));
        return client.ReportUsedAsync(ip.ToString(), cancellationToken);
    }

    private static AtlasFirewallProxyKeyEntry BuildEntry(MongoDbApiAccess access, bool canManage)
    {
        return new AtlasFirewallProxyKeyEntry
        {
            ApiKey = access.Quilt4NetApiKey,
            GroupId = access.GroupId,
            CanManage = canManage,
        };
    }
}

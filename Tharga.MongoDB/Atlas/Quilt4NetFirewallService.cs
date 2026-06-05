using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

/// <summary>
/// Thin adapter that translates from <see cref="MongoDbApiAccess"/> records to the lean
/// <see cref="Quilt4NetFirewallProxyClient"/> HTTP calls. Kept as a distinct seam so the
/// dispatch site doesn't need to know about HttpClient or wire-format details.
/// </summary>
internal sealed class Quilt4NetFirewallService
{
    private readonly Quilt4NetFirewallProxyClient _proxy;

    public Quilt4NetFirewallService(Quilt4NetFirewallProxyClient proxy)
    {
        _proxy = proxy;
    }

    public Task<FirewallProxyOpenResponse> OpenAsync(MongoDbApiAccess access, IPAddress ip, string name = null, CancellationToken cancellationToken = default)
    {
        return _proxy.OpenAsync(access.Quilt4NetBaseUrl, access.Quilt4NetApiKey, access.GroupId, ip, name, cancellationToken);
    }

    public Task<FirewallProxyUsageResponse> ReportUsedAsync(MongoDbApiAccess access, IPAddress ip, CancellationToken cancellationToken = default)
    {
        return _proxy.ReportUsedAsync(access.Quilt4NetBaseUrl, access.Quilt4NetApiKey, access.GroupId, ip, cancellationToken);
    }
}

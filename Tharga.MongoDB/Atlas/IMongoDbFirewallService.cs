using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

internal class MongoDbFirewallStateService : IMongoDbFirewallStateService
{
    private readonly IMongoDbFirewallService _mongoDbFirewallService;
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private FirewallResponse _result;

    public MongoDbFirewallStateService(IMongoDbFirewallService mongoDbFirewallService)
    {
        _mongoDbFirewallService = mongoDbFirewallService;
    }

    public async ValueTask AssureFirewallAccessAsync(MongoDbApiAccess accessInfo, bool force = false)
    {
        var previousIpAddress = force ? _result?.IpAddress : null;

        if (!force && _result != null) return;

        try
        {
            await _semaphoreSlim.WaitAsync();

            if (!force && _result != null) return;
            if (!Equals(previousIpAddress, _result?.IpAddress)) return;

            _result = await _mongoDbFirewallService.AssureFirewallAccessAsync(accessInfo);
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
}

internal interface IMongoDbFirewallStateService
{
    ValueTask AssureFirewallAccessAsync(MongoDbApiAccess accessInfo, bool force = false);
}

internal interface IMongoDbFirewallService
{
    Task<FirewallResponse> AssureFirewallAccessAsync(MongoDbApiAccess access, string name = null);
    IAsyncEnumerable<WhiteListItem> GetFirewallListAsync(MongoDbApiAccess access);
    Task RemoveFromFirewallAsync(MongoDbApiAccess access, string name);
    Task AddToFirewallAsync(MongoDbApiAccess access, string name, IPAddress ipAddress);
}

public record FirewallResponse
{
    public IPAddress IpAddress { get; init; }
    public string Name { get; init; }
    public EResult Result { get; set; }

    public enum EResult{ NoAccessProvided, AlreadyOpen, Open }
}
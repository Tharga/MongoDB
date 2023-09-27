using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

internal class MongoDbFirewallStateService : IMongoDbFirewallStateService
{
    private readonly IMongoDbFirewallService _mongoDbFirewallService;
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private readonly ConcurrentDictionary<MongoDbApiAccess, FirewallResponse> _dictionary = new();

    public MongoDbFirewallStateService(IMongoDbFirewallService mongoDbFirewallService)
    {
        _mongoDbFirewallService = mongoDbFirewallService;
    }

    public async ValueTask AssureFirewallAccessAsync(MongoDbApiAccess accessInfo, bool force = false)
    {
        _dictionary.TryGetValue(accessInfo, out var current);
        if (!force && current != null) return;

        try
        {
            await _semaphoreSlim.WaitAsync();

            _dictionary.TryGetValue(accessInfo, out var updated);
            if (!force && updated != null) return;
            if (!Equals(current?.IpAddress, updated?.IpAddress)) return;

            var result = await _mongoDbFirewallService.AssureFirewallAccessAsync(accessInfo);
            _dictionary.AddOrUpdate(accessInfo, result, (_, _) => result);
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
}
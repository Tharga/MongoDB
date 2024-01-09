using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

internal class MongoDbFirewallStateService : IMongoDbFirewallStateService
{
    private readonly IMongoDbFirewallService _mongoDbFirewallService;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private readonly ConcurrentDictionary<MongoDbApiAccess, FirewallResponse> _dictionary = new();

    public MongoDbFirewallStateService(IMongoDbFirewallService mongoDbFirewallService, IHostEnvironment hostEnvironment)
    {
        _mongoDbFirewallService = mongoDbFirewallService;
        _hostEnvironment = hostEnvironment;
    }

    public async ValueTask<string> AssureFirewallAccessAsync(MongoDbApiAccess accessInfo, bool force = false)
    {
        if (!accessInfo.HasMongoDbApiAccess()) return "No information.";

        _dictionary.TryGetValue(accessInfo, out var current);
        if (!force && current != null) return $"Already verified with result '{current.Result}' for {current.Name} with IP {current.IpAddress}.";

        try
        {
            await _semaphoreSlim.WaitAsync();

            _dictionary.TryGetValue(accessInfo, out var updated);
            if (!force && updated != null) return $"Already verified with result '{updated.Result}' for {updated.Name} with IP {updated.IpAddress} (Waited for other thread).";
            if (!Equals(current?.IpAddress, updated?.IpAddress)) return $"Ip address changed from '{current?.IpAddress}' to '{updated?.IpAddress}' when waiting for thread for {accessInfo.Name}.";

            var result = await _mongoDbFirewallService.AssureFirewallAccessAsync(accessInfo, BuildName(accessInfo));
            _dictionary.AddOrUpdate(accessInfo, result, (_, _) => result);
            return $"Firewall api responded with '{result.Result}' for {result.Name} with IP {result.IpAddress}.";
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    private string BuildName(MongoDbApiAccess accessInfo)
    {
        var environment = _hostEnvironment.EnvironmentName == "Production" ? null : $"-{_hostEnvironment.EnvironmentName}";
        var machineName = Environment.MachineName;

        var result = accessInfo.Name?
            .Replace("{machineName}", machineName, StringComparison.InvariantCultureIgnoreCase)
            .Replace("{environment}", environment, StringComparison.InvariantCultureIgnoreCase);

        if (string.IsNullOrEmpty(result)) result = $"{machineName}{environment}-Auto";
        return result;
    }
}
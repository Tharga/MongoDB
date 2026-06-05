using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

internal class MongoDbFirewallStateService : IMongoDbFirewallStateService
{
    private static readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IMongoDbFirewallService _mongoDbFirewallService;
    private readonly IExternalIpAddressService _externalIpAddressService;
    private readonly Quilt4NetFirewallService _quilt4Net;
    private readonly Quilt4NetHeartbeatService _heartbeat;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ConcurrentDictionary<MongoDbApiAccess, FirewallResponse> _dictionary = new();

    public MongoDbFirewallStateService(
        IMongoDbFirewallService mongoDbFirewallService,
        IExternalIpAddressService externalIpAddressService,
        Quilt4NetFirewallService quilt4Net,
        Quilt4NetHeartbeatService heartbeat,
        IHostEnvironment hostEnvironment)
    {
        _mongoDbFirewallService = mongoDbFirewallService;
        _externalIpAddressService = externalIpAddressService;
        _quilt4Net = quilt4Net;
        _heartbeat = heartbeat;
        _hostEnvironment = hostEnvironment;
    }

    public async ValueTask<string> AssureFirewallAccessAsync(MongoDbApiAccess accessInfo, bool force = false)
    {
        var mode = accessInfo.GetFirewallMode();
        if (mode == FirewallMode.None) return "No information.";

        _dictionary.TryGetValue(accessInfo, out var current);
        if (!force && current != null) return $"Already verified with result '{current.Result}' for {current.Name} with IP {current.IpAddress}.";

        try
        {
            await _lock.WaitAsync();

            _dictionary.TryGetValue(accessInfo, out var updated);
            if (!force && updated != null) return $"Already verified with result '{updated.Result}' for {updated.Name} with IP {updated.IpAddress} (Waited for other thread).";
            if (!Equals(current?.IpAddress, updated?.IpAddress)) return $"Ip address changed from '{current?.IpAddress}' to '{updated?.IpAddress}' when waiting for thread for {accessInfo.Name}.";

            return mode switch
            {
                FirewallMode.Classic => await OpenViaAtlasAsync(accessInfo),
                FirewallMode.Notify => await OpenViaAtlasAndHeartbeatAsync(accessInfo),
                FirewallMode.Open => await OpenViaQuilt4NetAsync(accessInfo),
                _ => "No information.",
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<string> OpenViaAtlasAsync(MongoDbApiAccess accessInfo)
    {
        var result = await _mongoDbFirewallService.AssureFirewallAccessAsync(accessInfo, BuildName(accessInfo));
        _dictionary.AddOrUpdate(accessInfo, result, (_, _) => result);
        return $"Firewall api responded with '{result.Result}' for {result.Name} with IP {result.IpAddress}.";
    }

    private async Task<string> OpenViaAtlasAndHeartbeatAsync(MongoDbApiAccess accessInfo)
    {
        var result = await _mongoDbFirewallService.AssureFirewallAccessAsync(accessInfo, BuildName(accessInfo));
        _dictionary.AddOrUpdate(accessInfo, result, (_, _) => result);
        if (result.IpAddress != null)
        {
            _heartbeat.Register(accessInfo, result.IpAddress, FirewallMode.Notify);
        }
        return $"Firewall api responded with '{result.Result}' for {result.Name} with IP {result.IpAddress} (Quilt4Net notify mode).";
    }

    private async Task<string> OpenViaQuilt4NetAsync(MongoDbApiAccess accessInfo)
    {
        var ip = await _externalIpAddressService.GetExternalIpAddressAsync();
        if (ip == null)
        {
            return "Quilt4Net Open mode: external IP could not be resolved; skipping open.";
        }

        var openResult = await _quilt4Net.OpenAsync(accessInfo, ip);
        _heartbeat.Register(accessInfo, ip, FirewallMode.Open);

        var response = new FirewallResponse
        {
            Result = MapQuilt4NetOutcome(openResult),
            Name = BuildName(accessInfo),
            IpAddress = ip,
        };
        _dictionary.AddOrUpdate(accessInfo, response, (_, _) => response);
        return $"Quilt4Net firewall responded with '{response.Result}' for {response.Name} with IP {response.IpAddress}.";
    }

    private static EFirewallOpenResult MapQuilt4NetOutcome(Quilt4Net.Toolkit.Features.Atlas.FirewallOpenResult openResult)
    {
        var name = openResult?.Outcome.ToString();
        return name switch
        {
            "Opened" => EFirewallOpenResult.Open,
            "AlreadyOpen" => EFirewallOpenResult.AlreadyOpen,
            _ => EFirewallOpenResult.NoAccessProvided,
        };
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
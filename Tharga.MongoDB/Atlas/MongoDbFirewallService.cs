using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

internal class MongoDbFirewallService : IMongoDbFirewallService
{
    private readonly IExternalIpAddressService _externalIpAddressService;
    private readonly ILogger<MongoDbFirewallService> _logger;

    public MongoDbFirewallService(IExternalIpAddressService externalIpAddressService, ILogger<MongoDbFirewallService> logger)
    {
        _externalIpAddressService = externalIpAddressService;
        _logger = logger;
    }

    public async Task<FirewallResponse> AssureFirewallAccessAsync(MongoDbApiAccess access, string name = null)
    {
        if (!access.HasMongoDbApiAccess()) return new FirewallResponse { Result = EFirewallOpenResult.NoAccessProvided };

        IPAddress ipAddress = null;
        try
        {
            ipAddress = await _externalIpAddressService.GetExternalIpAddressAsync();
            name ??= $"{Environment.MachineName}-Auto";

            var items = await GetFirewallListAsync(access).ToArrayAsync();
            var existing = items.FirstOrDefault(x => x.CidrBlock.StartsWith(ipAddress.ToString()));
            if (existing != null)
            {
                _logger?.LogTrace("Firewall already open for '{ipAddress}' with name {name}.", ipAddress, existing.Comment);
                return new FirewallResponse { Name = name, IpAddress = ipAddress, Result = EFirewallOpenResult.AlreadyOpen };
            }

            await RemoveFromFirewallAsync(access, name);
            await AddToFirewallAsync(access, name, ipAddress);

            return new FirewallResponse { Name = name, IpAddress = ipAddress, Result = EFirewallOpenResult.Open };
        }
        catch (Exception e)
        {
            _logger?.LogError(e, "Unable to AsureAccess to Atlas MongoDB for ip '{externalIp}' with name {name}. {details}", ipAddress, name, e.Message);
            //ActionEvent?.Invoke(this, new ActionEventArgs(new ActionEventArgs.ActionData { Level = LogLevel.Error, Message = $"Unable to AsureAccess to Atlas MongoDB for ip '{ipAddress}'. {e.Message}" }, null));
            Debugger.Break();
            Console.WriteLine(e);
            throw;
        }
    }

    public async Task<(bool HaveAccess, string Message)> HaveAccess(MongoDbApiAccess access)
    {
        if (!access.HasMongoDbApiAccess()) return (false, "No configuration.");

        try
        {
            var ipAddress = await _externalIpAddressService.GetExternalIpAddressAsync();

            var items = await GetFirewallListAsync(access).ToArrayAsync();
            var existing = items.FirstOrDefault(x => x.CidrBlock.StartsWith(ipAddress.ToString()));
            if (existing != null)
            {
                return (true, $"Access with name '{existing.Comment}'.");
            }

            return (false, $"No access for ip '{ipAddress}'.");
        }
        catch (Exception e)
        {
            return (false, e.Message);
        }
    }

    public async IAsyncEnumerable<WhiteListItem> GetFirewallListAsync(MongoDbApiAccess access)
    {
        if (access == null) throw new ArgumentNullException(nameof(access));

        using var atlasHttp = new AtlasHttpClient(access);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"groups/{access.GroupId}/accessList");
        using var result = await atlasHttp.Client.SendAsync(request);
        result.EnsureSuccessStatusCode();
        var content = await result.Content.ReadFromJsonAsync<WhiteListResult>();
        if (content != null)
        {
            foreach (var item in content.Results)
            {
                yield return item;
            }
        }
    }

    public async Task RemoveFromFirewallAsync(MongoDbApiAccess access, string name)
    {
        if (access == null) throw new ArgumentNullException(nameof(access));

        using var atlasHttp = new AtlasHttpClient(access);

        await foreach (var item in GetFirewallListAsync(access).Where(x => x.Comment == name))
        {
            var r = await atlasHttp.Client.DeleteAsync($"groups/{access.GroupId}/accessList/{item.IpAddress}");
            _logger.LogInformation("Firewall entry removed for '{name}' with ip '{ipAddress}'.", name, item.IpAddress);
            r.EnsureSuccessStatusCode();
        }
    }

    public async Task AddToFirewallAsync(MongoDbApiAccess access, string name, IPAddress ipAddress)
    {
        if (access == null) throw new ArgumentNullException(nameof(access));

        using var atlasHttp = new AtlasHttpClient(access);

        var payload = new[] { new { cidrBlock = $"{ipAddress}/32", comment = name } };
        var serialized = JsonSerializer.Serialize(payload);
        using var content = new StringContent(serialized, Encoding.UTF8, "application/json");
        using var result = await atlasHttp.Client.PostAsync($"groups/{access.GroupId}/accessList", content);
        result.EnsureSuccessStatusCode();
        _logger.LogInformation("Firewall opened for ip '{ipAddress}' with comment '{name}'.", ipAddress, name);
    }
}
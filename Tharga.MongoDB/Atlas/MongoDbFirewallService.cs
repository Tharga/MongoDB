using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
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
        if (!access.HasMongoDbApiAccess()) return new FirewallResponse { Result = FirewallResponse.EResult.AlreadyOpen };

        try
        {
            var ipAddress = await _externalIpAddressService.GetExternalIpAddressAsync();
            name ??= $"{Environment.MachineName}-Auto";

            var items = await GetFirewallListAsync(access).ToArrayAsync();
            var existing = items.FirstOrDefault(x => x.CidrBlock.StartsWith(ipAddress.ToString()));
            if (existing != null)
            {
                _logger?.LogTrace("Firewall already open for '{ipAddress}' with name {name}.", ipAddress, existing.Comment);
                return new FirewallResponse { Name = name, IpAddress = ipAddress, Result = FirewallResponse.EResult.AlreadyOpen };
            }

            await RemoveFromFirewallAsync(access, name);
            await AddToFirewallAsync(access, name, ipAddress);

            return new FirewallResponse { Name = name, IpAddress = ipAddress, Result = FirewallResponse.EResult.Open };
        }
        catch (Exception e)
        {
            //_logger?.LogError(e, "Unable to AsureAccess to Atlas MongoDB for ip '{externalIp}'. {details}", ipAddress, e.Message);
            //ActionEvent?.Invoke(this, new ActionEventArgs(new ActionEventArgs.ActionData { Level = LogLevel.Error, Message = $"Unable to AsureAccess to Atlas MongoDB for ip '{ipAddress}'. {e.Message}" }, null));
            Debugger.Break();
            Console.WriteLine(e);
            throw;
        }
    }

    public async IAsyncEnumerable<WhiteListItem> GetFirewallListAsync(MongoDbApiAccess access)
    {
        if (access == null) throw new ArgumentNullException(nameof(access));

        using var handler = new HttpClientHandler { Credentials = new NetworkCredential(access.PublicKey, access.PrivateKey) };
        using var httpClient = new HttpClient(handler);
        httpClient.BaseAddress = new Uri("https://cloud.mongodb.com/api/atlas/v1.0/");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"groups/{access.GroupId}/accessList");
        using var result = await httpClient.SendAsync(request);
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

    public Task RemoveFromFirewallAsync(MongoDbApiAccess access, string name)
    {
        //        access ??= _access ?? throw new InvalidOleVariantTypeException("Provide access info.");

        //        using var httpClient = GetHttpClient(access);

        //        await foreach (var item in GetWhitelist().Where(x => x.Comment == comment))
        //        {
        //            var r = await httpClient.DeleteAsync($"groups/{access.GroupId}/accessList/{item.IpAddress}");
        //            r.EnsureSuccessStatusCode();
        //        }
        throw new NotImplementedException();
    }

    public async Task AddToFirewallAsync(MongoDbApiAccess access, string name, IPAddress ipAddress)
    {
    //        access ??= _access ?? throw new InvalidOleVariantTypeException("Provide access info.");

    //        using var httpClient = GetHttpClient(access);

    //        var payload = new[] { new { cidrBlock = $"{ipAddress}/32", comment } };
    //        var ser = JsonSerializer.Serialize(payload);
    //        var content = new StringContent(ser, Encoding.UTF8, "application/json");
    //        var r = await httpClient.PostAsync($"groups/{access.GroupId}/accessList", content);
    //        r.EnsureSuccessStatusCode();
    //        _logger.LogInformation("Firewall opened for ip '{ipAddress}' with comment '{comment}'.", ipAddress, comment);
    //        ActionEvent?.Invoke(this, new ActionEventArgs(new ActionEventArgs.ActionData { Level = LogLevel.Information, Message = $"Firewall opened for ip '{ipAddress}' with comment '{comment}'." }, null));
    throw new NotImplementedException();
    }
}
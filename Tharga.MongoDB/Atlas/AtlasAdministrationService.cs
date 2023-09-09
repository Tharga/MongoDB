using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

internal class AtlasAdministrationService
{
    //NOTE: Read more here
    //- https://www.mongodb.com/docs/atlas/api/
    //- https://www.mongodb.com/docs/atlas/configure-api-access/
    //- https://www.mongodb.com/docs/atlas/configure-api-access/#std-label-create-org-api-key

    private readonly MongoDbApiAccess _access;
    private readonly ILogger _logger;

    public AtlasAdministrationService(MongoDbApiAccess access = null, ILogger logger = null)
    {
        _access = access;
        _logger = logger;
    }

    public static event EventHandler<ActionEventArgs> ActionEvent;

    public IEnumerable<WhiteListItem> GetWhitelist(MongoDbApiAccess access = null)
    {
        access ??= _access ?? throw new InvalidOleVariantTypeException("Provide access info.");

        using var httpClient = GetHttpClient(access);

        var r = httpClient.GetAsync($"groups/{access.GroupId}/accessList").GetAwaiter().GetResult();
        r.EnsureSuccessStatusCode();
        var rb = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        var result = JsonSerializer.Deserialize<WhiteListResult>(rb, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return result?.Results ?? Array.Empty<WhiteListItem>();
    }

    private HttpClient GetHttpClient(MongoDbApiAccess access)
    {
        access ??= _access ?? throw new InvalidOleVariantTypeException("Provide access info.");

        var httpClient = new HttpClient(new HttpClientHandler { Credentials = new NetworkCredential(access.PublicKey, access.PrivateKey) });
        httpClient.BaseAddress = new Uri("https://cloud.mongodb.com/api/atlas/v1.0/");
        return httpClient;
    }

    public void SetWhitelist(string comment, string ipAddress, MongoDbApiAccess access = null)
    {
        access ??= _access ?? throw new InvalidOleVariantTypeException("Provide access info.");

        using var httpClient = GetHttpClient(access);

        var payload = new[] { new { cidrBlock = $"{ipAddress}/32", comment } };
        var ser = JsonSerializer.Serialize(payload);
        var content = new StringContent(ser, Encoding.UTF8, "application/json");
        var r = httpClient.PostAsync($"groups/{access.GroupId}/accessList", content).GetAwaiter().GetResult();
        r.EnsureSuccessStatusCode();
        var message = $"Firewall opened for ip '{ipAddress}' with comment '{comment}'.";
        _logger.LogInformation(message);
        ActionEvent?.Invoke(this, new ActionEventArgs(new ActionEventArgs.ActionData { Level = LogLevel.Information, Message = message }, null));
    }

    public IPAddress GetExternalIpAddress()
    {
        using var httpClient = new HttpClient();
        var r = httpClient.GetAsync(new Uri("https://ipv4.icanhazip.com/")).GetAwaiter().GetResult();
        r.EnsureSuccessStatusCode();
        var externalIpString = r.Content.ReadAsStringAsync().GetAwaiter().GetResult().Replace("\\r\\n", "").Replace("\\n", "").Trim();
        var externalIp = IPAddress.Parse(externalIpString);
        return externalIp;
    }

    internal record WhiteListResult
    {
        public WhiteListItem[] Results { get; init; }
    }

    public record WhiteListItem
    {
        public string IpAddress { get; init; }
        public string CidrBlock { get; init; }
        public string Comment { get; init; }
        public string GroupId { get; init; }
    }

    public IPAddress AssureAccess(string comment = null)
    {
        IPAddress externalIp = null;
        try
        {
            externalIp = GetExternalIpAddress();
            var machineName = comment ?? Environment.MachineName;

            var items = GetWhitelist();
            if (items.Any(x => x.CidrBlock.StartsWith(externalIp.ToString())))
            {
                var message = $"Firewall already open for ip '{externalIp}'.";
                _logger?.LogTrace(message);
                ActionEvent?.Invoke(this, new ActionEventArgs(new ActionEventArgs.ActionData { Level = LogLevel.Information, Message = message }, null));
                return externalIp;
            }

            RemoveWhitelist(machineName);
            SetWhitelist(machineName, externalIp.ToString());
            return externalIp;
        }
        catch (Exception e)
        {
            var message = $"Unable to AsureAccess to Atlas MongoDB for ip '{externalIp}'. {e.Message}";
            _logger?.LogError(e, message);
            ActionEvent?.Invoke(this, new ActionEventArgs(new ActionEventArgs.ActionData { Level = LogLevel.Error, Message = message }, null));
            return null;
        }
    }

    public void RemoveWhitelist(string comment, MongoDbApiAccess access = null)
    {
        access ??= _access ?? throw new InvalidOleVariantTypeException("Provide access info.");

        using var httpClient = GetHttpClient(access);

        foreach (var item in GetWhitelist().Where(x => x.Comment == comment))
        {
            var r = httpClient.DeleteAsync($"groups/{access.GroupId}/accessList/{item.IpAddress}").GetAwaiter().GetResult();
            r.EnsureSuccessStatusCode();
        }
    }
}
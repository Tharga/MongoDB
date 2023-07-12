using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

internal class AtlasAdministrationService
{
    //NOTE: Read more here
    //- https://www.mongodb.com/docs/atlas/api/
    //- https://www.mongodb.com/docs/atlas/configure-api-access/
    //- https://www.mongodb.com/docs/atlas/configure-api-access/#std-label-create-org-api-key

    private readonly MongoDbApiAccess _access;

    public AtlasAdministrationService(MongoDbApiAccess access = null)
    {
        _access = access;
    }

    public IEnumerable<WhiteListItem> GetWhitelist(MongoDbApiAccess access = null)
    {
        access ??= _access ?? throw new InvalidOleVariantTypeException("Provide access info.");

        using var httpClient = GetHttpClient(access);

        var r = httpClient.GetAsync($"groups/{access.ClusterId}/accessList").GetAwaiter().GetResult();
        r.EnsureSuccessStatusCode();
        var rb = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        var result = JsonSerializer.Deserialize<WhiteListResult>(rb, new JsonSerializerOptions { PropertyNameCaseInsensitive = false });
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
        var r = httpClient.PostAsync($"groups/{access.ClusterId}/accessList", content).GetAwaiter().GetResult();
        r.EnsureSuccessStatusCode();
    }

    public IPAddress GetExternalIpAddress()
    {
        using var httpClient = new HttpClient();
        var r = httpClient.GetAsync(new Uri("http://icanhazip.com")).GetAwaiter().GetResult();
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
        var externalIp = GetExternalIpAddress();
        var machineName = comment ?? Environment.MachineName;

        var items = GetWhitelist();
        if (items.Any(x => x.CidrBlock.StartsWith(externalIp.ToString()))) return externalIp;

        RemoveWhitelist(machineName);
        SetWhitelist(machineName, externalIp.ToString());
        return externalIp;
    }

    public void RemoveWhitelist(string comment, MongoDbApiAccess access = null)
    {
        access ??= _access ?? throw new InvalidOleVariantTypeException("Provide access info.");

        using var httpClient = GetHttpClient(access);

        foreach (var item in GetWhitelist().Where(x => x.Comment == comment))
        {
            var r = httpClient.DeleteAsync($"groups/{access.ClusterId}/accessList/{item.IpAddress}").GetAwaiter().GetResult();
            r.EnsureSuccessStatusCode();
        }
    }
}
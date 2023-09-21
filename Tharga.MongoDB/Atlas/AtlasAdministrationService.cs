using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly string[] _uris = { "https://ipv4.icanhazip.com", "http://icanhazip.com" };

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

    private async Task<IPAddress> GetExternalIpAddress()
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            foreach (var uri in _uris)
            {
                try
                {
                    var result = await httpClient.GetAsync(new Uri(uri), tokenSource.Token);
                    if (result.IsSuccessStatusCode)
                    {
                        var externalIpString = (await result.Content.ReadAsStringAsync(CancellationToken.None)).Replace("\\r\\n", "").Replace("\\n", "").Trim();
                        var externalIp = IPAddress.Parse(externalIpString);
                        return externalIp;
                    }
                }
                catch (HttpRequestException e)
                {
                    _logger.LogWarning(e, e.Message);
                }
            }

            return null;
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
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

    public async Task<IPAddress> AssureAccess(string comment = null)
    {
        IPAddress externalIp = null;
        try
        {
            externalIp = await GetExternalIpAddress();
            var machineName = comment ?? Environment.MachineName;

            var items = GetWhitelist();
            if (items.Any(x => x.CidrBlock.StartsWith(externalIp.ToString())))
            {
                var message = $"Firewall already open for ip '{externalIp}'.";
                _logger?.LogTrace(message);
                ActionEvent?.Invoke(this, new ActionEventArgs(new ActionEventArgs.ActionData { Level = LogLevel.Information, Message = message }, null));
                return externalIp;
            }

            await RemoveWhitelist(machineName);
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

    public async Task RemoveWhitelist(string comment, MongoDbApiAccess access = null)
    {
        access ??= _access ?? throw new InvalidOleVariantTypeException("Provide access info.");

        using var httpClient = GetHttpClient(access);

        foreach (var item in GetWhitelist().Where(x => x.Comment == comment))
        {
            var r = await httpClient.DeleteAsync($"groups/{access.GroupId}/accessList/{item.IpAddress}");
            r.EnsureSuccessStatusCode();
        }
    }
}
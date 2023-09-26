//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Net;
//using System.Net.Http;
//using System.Runtime.InteropServices;
//using System.Text;
//using System.Text.Json;
//using System.Threading.Tasks;
//using Microsoft.Extensions.Logging;
//using Tharga.MongoDB.Configuration;

//namespace Tharga.MongoDB.Atlas;

////internal interface IAtlasAdministrationService
////{
////    Task<IPAddress> AssureAccess(string comment = null);
////}

//internal class AtlasAdministrationService //: IAtlasAdministrationService
//{
//    //NOTE: Read more here
//    //- https://www.mongodb.com/docs/atlas/api/
//    //- https://www.mongodb.com/docs/atlas/configure-api-access/
//    //- https://www.mongodb.com/docs/atlas/configure-api-access/#std-label-create-org-api-key

//    //private readonly IHttpClientFactory _httpClientFactory;
//    private readonly MongoDbApiAccess _access;
//    private readonly ILogger _logger;
//    //private readonly string[] _uris = { "https://ipv4.icanhazip.com", "http://icanhazip.com", "https://app-eplicta-aggregator-prod.azurewebsites.net/api/IpAddress", "https://quilt4net.com/api/IpAddress" };

//    public AtlasAdministrationService(MongoDbApiAccess access = null, ILogger logger = null)
//    {
//        _access = access;
//        _logger = logger;
//    }

//    public static event EventHandler<ActionEventArgs> ActionEvent;

//    public async IAsyncEnumerable<WhiteListItem> GetWhitelist(MongoDbApiAccess access = null)
//    {
//        access ??= _access ?? throw new InvalidOleVariantTypeException("Provide access info.");

//        using var httpClient = GetHttpClient(access);

//        var r = await httpClient.GetAsync($"groups/{access.GroupId}/accessList");
//        r.EnsureSuccessStatusCode();
//        var rb = await r.Content.ReadAsStringAsync();

//        var result = JsonSerializer.Deserialize<WhiteListResult>(rb, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
//        if (result != null)
//        {
//            foreach (var item in result.Results)
//            {
//                yield return item;
//            }
//        }
//    }

//    private HttpClient GetHttpClient(MongoDbApiAccess access)
//    {
//        access ??= _access ?? throw new InvalidOleVariantTypeException("Provide access info.");

//        var httpClient = new HttpClient(new HttpClientHandler { Credentials = new NetworkCredential(access.PublicKey, access.PrivateKey) }, true);
//        httpClient.BaseAddress = new Uri("https://cloud.mongodb.com/api/atlas/v1.0/");
//        return httpClient;
//    }

//    public async Task SetWhitelist(string comment, string ipAddress, MongoDbApiAccess access = null)
//    {
//        access ??= _access ?? throw new InvalidOleVariantTypeException("Provide access info.");

//        using var httpClient = GetHttpClient(access);

//        var payload = new[] { new { cidrBlock = $"{ipAddress}/32", comment } };
//        var ser = JsonSerializer.Serialize(payload);
//        var content = new StringContent(ser, Encoding.UTF8, "application/json");
//        var r = await httpClient.PostAsync($"groups/{access.GroupId}/accessList", content);
//        r.EnsureSuccessStatusCode();
//        _logger.LogInformation("Firewall opened for ip '{ipAddress}' with comment '{comment}'.", ipAddress, comment);
//        ActionEvent?.Invoke(this, new ActionEventArgs(new ActionEventArgs.ActionData { Level = LogLevel.Information, Message = $"Firewall opened for ip '{ipAddress}' with comment '{comment}'." }, null));
//    }

//    //private async Task<IPAddress> GetExternalIpAddress()
//    //{
//    //    try
//    //    {
//    //        foreach (var uri in _uris)
//    //        {
//    //            try
//    //            {
//    //                using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
//    //                using var httpClient = _httpClientFactory.CreateClient("IpAddressClient");
//    //                var result = await httpClient.GetAsync(new Uri(uri), tokenSource.Token);
//    //                if (result.IsSuccessStatusCode)
//    //                {
//    //                    var externalIpString = (await result.Content.ReadAsStringAsync(CancellationToken.None)).Replace("\\r\\n", "").Replace("\\n", "").Trim();
//    //                    var externalIp = IPAddress.Parse(externalIpString);
//    //                    return externalIp;
//    //                }
//    //            }
//    //            catch (TaskCanceledException e)
//    //            {
//    //                _logger.LogWarning(e, $"Failed to call '{{uri}}'. {e.Message}", uri);
//    //            }
//    //            catch (HttpRequestException e)
//    //            {
//    //                _logger.LogWarning(e, $"Failed to call '{{uri}}'. {e.Message}", uri);
//    //            }
//    //            catch (Exception e)
//    //            {
//    //                Debugger.Break();
//    //                throw;
//    //            }
//    //        }

//    //        return null;
//    //    }
//    //    catch (Exception e)
//    //    {
//    //        _logger.LogError(e, e.Message);
//    //        throw;
//    //    }
//    //}

//    internal record WhiteListResult
//    {
//        public WhiteListItem[] Results { get; init; }
//    }

//    public record WhiteListItem
//    {
//        public string IpAddress { get; init; }
//        public string CidrBlock { get; init; }
//        public string Comment { get; init; }
//        public string GroupId { get; init; }
//    }

//    public async Task<IPAddress> AssureAccess(IPAddress ipAddress, string comment = null)
//    {
//        //IPAddress externalIp = null;
//        try
//        {
//            //externalIp = await GetExternalIpAddress();
//            var machineName = comment ?? Environment.MachineName;

//            var items = await GetWhitelist().ToArrayAsync();
//            if (items.Any(x => x.CidrBlock.StartsWith(ipAddress.ToString())))
//            {
//                var message = $"Firewall already open for ip '{ipAddress}'.";
//                _logger?.LogTrace(message);
//                ActionEvent?.Invoke(this, new ActionEventArgs(new ActionEventArgs.ActionData { Level = LogLevel.Information, Message = message }, null));
//                return ipAddress;
//            }

//            await RemoveWhitelist(machineName);
//            await SetWhitelist(machineName, ipAddress.ToString());
//            return ipAddress;
//        }
//        catch (Exception e)
//        {
//            _logger?.LogError(e, "Unable to AsureAccess to Atlas MongoDB for ip '{externalIp}'. {details}", ipAddress, e.Message);
//            ActionEvent?.Invoke(this, new ActionEventArgs(new ActionEventArgs.ActionData { Level = LogLevel.Error, Message = $"Unable to AsureAccess to Atlas MongoDB for ip '{ipAddress}'. {e.Message}" }, null));
//            return null;
//        }
//    }

//    public async Task RemoveWhitelist(string comment, MongoDbApiAccess access = null)
//    {
//        access ??= _access ?? throw new InvalidOleVariantTypeException("Provide access info.");

//        using var httpClient = GetHttpClient(access);

//        await foreach (var item in GetWhitelist().Where(x => x.Comment == comment))
//        {
//            var r = await httpClient.DeleteAsync($"groups/{access.GroupId}/accessList/{item.IpAddress}");
//            r.EnsureSuccessStatusCode();
//        }
//    }
//}
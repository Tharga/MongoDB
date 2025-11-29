using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Tharga.MongoDB.Atlas;

internal class ExternalIpAddressService : IExternalIpAddressService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ExternalIpAddressService> _logger;

    private readonly string[] _uris = { "https://ipv4.icanhazip.com", "http://icanhazip.com", "https://quilt4net.com/api/IpAddress" };

    public ExternalIpAddressService(IHttpClientFactory httpClientFactory, ILogger<ExternalIpAddressService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IPAddress> GetExternalIpAddressAsync()
    {
        try
        {
            foreach (var uri in _uris)
            {
                try
                {
                    using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var httpClient = _httpClientFactory.CreateClient("Tharga.MongoDB.IpAddressClient");
                    var result = await httpClient.GetAsync(new Uri(uri), tokenSource.Token);
                    if (result.IsSuccessStatusCode)
                    {
                        var externalIpString = (await result.Content.ReadAsStringAsync(CancellationToken.None)).Replace("\\r\\n", "").Replace("\\n", "").Trim();
                        var externalIp = IPAddress.Parse(externalIpString);
                        return externalIp;
                    }
                }
                catch (TaskCanceledException e)
                {
                    _logger.LogWarning(e, $"Failed to call '{{uri}}'. {e.Message}", uri);
                }
                catch (HttpRequestException e)
                {
                    _logger.LogWarning(e, $"Failed to call '{{uri}}'. {e.Message}", uri);
                }
                catch (Exception)
                {
                    Debugger.Break();
                    throw;
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
}
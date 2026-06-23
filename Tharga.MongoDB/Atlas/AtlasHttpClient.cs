using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

internal class AtlasHttpClient : IDisposable
{
    private static readonly Uri BaseAddress = new("https://cloud.mongodb.com/api/atlas/v1.0/");
    private readonly HttpClient _httpClient;
    private readonly HttpClientHandler _handler;

    private AtlasHttpClient(HttpClient client, HttpClientHandler handler)
    {
        _httpClient = client;
        _handler = handler;
    }

    public HttpClient Client => _httpClient;

    public static async Task<AtlasHttpClient> CreateAsync(MongoDbApiAccess access, IAtlasTokenService tokenService, CancellationToken cancellationToken = default)
    {
        var handler = new HttpClientHandler();
        if (access.UsesServiceAccount())
        {
            var token = await tokenService.GetAccessTokenAsync(access, cancellationToken);
            var client = new HttpClient(handler) { BaseAddress = BaseAddress };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return new AtlasHttpClient(client, handler);
        }

        handler.Credentials = new NetworkCredential(access.PublicKey, access.PrivateKey);
        return new AtlasHttpClient(new HttpClient(handler) { BaseAddress = BaseAddress }, handler);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _handler.Dispose();
    }
}

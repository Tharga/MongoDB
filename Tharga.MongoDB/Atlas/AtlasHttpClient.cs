using System;
using System.Net;
using System.Net.Http;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

internal class AtlasHttpClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly HttpClientHandler _handler;

    public AtlasHttpClient(MongoDbApiAccess access)
    {
        _handler = new HttpClientHandler();
        _handler.Credentials = new NetworkCredential(access.PublicKey, access.PrivateKey);
        _httpClient = new HttpClient(_handler);
        _httpClient.BaseAddress = new Uri("https://cloud.mongodb.com/api/atlas/v1.0/");
    }

    public HttpClient Client => _httpClient;

    public void Dispose()
    {
        _httpClient.Dispose();
        _handler.Dispose();
    }
}
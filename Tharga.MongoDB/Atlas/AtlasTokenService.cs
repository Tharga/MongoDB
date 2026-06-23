using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

internal sealed class AtlasTokenService : IAtlasTokenService
{
    internal const string HttpClientName = "Tharga.MongoDB.AtlasOAuth";
    private static readonly Uri TokenEndpoint = new("https://cloud.mongodb.com/api/oauth/token");
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(60);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AtlasTokenService> _logger;
    private readonly ConcurrentDictionary<string, (string Token, DateTime ExpiresUtc)> _tokenCache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public AtlasTokenService(IHttpClientFactory httpClientFactory, ILogger<AtlasTokenService> logger = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(MongoDbApiAccess access, CancellationToken cancellationToken = default)
    {
        if (access == null) throw new ArgumentNullException(nameof(access));

        if (TryGetCached(access.ClientId, out var cached)) return cached;

        var gate = _locks.GetOrAdd(access.ClientId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetCached(access.ClientId, out cached)) return cached;

            var (token, expiresInSeconds) = await RequestTokenAsync(access, cancellationToken).ConfigureAwait(false);
            _tokenCache[access.ClientId] = (token, DateTime.UtcNow.AddSeconds(expiresInSeconds));
            return token;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryGetCached(string clientId, out string token)
    {
        token = null;
        if (!_tokenCache.TryGetValue(clientId, out var entry)) return false;
        if (DateTime.UtcNow >= entry.ExpiresUtc - ExpiryMargin) return false;
        token = entry.Token;
        return true;
    }

    private async Task<(string Token, int ExpiresInSeconds)> RequestTokenAsync(MongoDbApiAccess access, CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{access.ClientId}:{access.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var likelyExpired = body != null && body.Contains("expire", StringComparison.OrdinalIgnoreCase);
            _logger?.LogError("Atlas service account token request failed with {statusCode}. LikelyExpired={likelyExpired}.", (int)response.StatusCode, likelyExpired);
            throw new AtlasServiceAccountAuthException(response.StatusCode, likelyExpired);
        }

        var result = await response.Content.ReadFromJsonAsync<AtlasTokenResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
        return (result.AccessToken, result.ExpiresIn);
    }
}

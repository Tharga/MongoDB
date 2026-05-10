using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Mcp.Atlas;

/// <summary>
/// Thin <see cref="HttpClient"/> wrapper for the MongoDB Atlas Administration API v2.
/// Uses HTTP Digest auth via public/private API keys, the same pattern as the firewall feature
/// in <c>Tharga.MongoDB</c>. Long-lived; register as a singleton.
/// </summary>
internal sealed class AtlasV2HttpClient : IDisposable
{
    // Atlas v2 uses calendar-versioned media types; pick a stable date that the public API
    // contract will continue to honor. Bump when we want to opt into newer fields.
    private const string MediaType = "application/vnd.atlas.2024-08-05+json";
    private const string BaseUrl = "https://cloud.mongodb.com/api/atlas/v2/";

    private readonly HttpClient _client;
    private readonly HttpMessageHandler _ownedHandler;

    /// <param name="access">Atlas API access credentials. PublicKey/PrivateKey are required; GroupId is consumed by the tool layer.</param>
    /// <param name="handler">
    /// Optional message handler. Tests pass a mock handler to intercept requests; production leaves this null
    /// and the client builds its own <see cref="HttpClientHandler"/> with digest credentials.
    /// </param>
    public AtlasV2HttpClient(MongoDbApiAccess access, HttpMessageHandler handler = null)
    {
        if (access == null) throw new ArgumentNullException(nameof(access));
        if (string.IsNullOrEmpty(access.PublicKey)) throw new ArgumentException("PublicKey is required.", nameof(access));
        if (string.IsNullOrEmpty(access.PrivateKey)) throw new ArgumentException("PrivateKey is required.", nameof(access));

        if (handler == null)
        {
            _ownedHandler = new HttpClientHandler
            {
                Credentials = new NetworkCredential(access.PublicKey, access.PrivateKey),
            };
            _client = new HttpClient(_ownedHandler);
        }
        else
        {
            // Caller-supplied handler — tests own its lifetime.
            _client = new HttpClient(handler, disposeHandler: false);
        }

        _client.BaseAddress = new Uri(BaseUrl);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaType));
    }

    /// <summary>
    /// GETs <paramref name="relativePath"/> and returns the parsed JSON body (cloned so callers can keep it past the document's lifetime).
    /// Throws if the response is not 2xx.
    /// </summary>
    public async Task<JsonElement> GetJsonAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(relativePath, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return doc.RootElement.Clone();
    }

    public void Dispose()
    {
        _client.Dispose();
        _ownedHandler?.Dispose();
    }
}

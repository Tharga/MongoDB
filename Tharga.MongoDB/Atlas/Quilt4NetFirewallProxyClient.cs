using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tharga.MongoDB.Atlas;

/// <summary>
/// Lean HTTP client for Quilt4Net's firewall proxy endpoints. Hand-rolled — no Quilt4Net.Toolkit
/// dependency, so the build order and release cadence stay decoupled. Wire format mirrors
/// <c>Quilt4Net.Toolkit.Features.Atlas.AtlasFirewallClient</c>:
/// <list type="bullet">
///   <item>POST <c>Api/AtlasFirewall/open</c>  body <c>{ groupId, ip, name }</c> → <c>{ Outcome }</c></item>
///   <item>POST <c>Api/AtlasFirewall/used</c>  body <c>{ groupId, ip }</c>          → <c>{ Outcome }</c></item>
/// </list>
/// Authenticates via <c>X-API-KEY</c>. 401/403 surfaces as
/// <see cref="Quilt4NetFirewallAuthorizationException"/>.
/// </summary>
internal sealed class Quilt4NetFirewallProxyClient
{
    public const string DefaultBaseUrl = "https://quilt4net.com/";
    internal const string HttpClientName = "Tharga.MongoDB.Quilt4NetFirewall";
    private readonly IHttpClientFactory _httpClientFactory;

    public Quilt4NetFirewallProxyClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public Task<FirewallProxyOpenResponse> OpenAsync(string baseUrl, string apiKey, string groupId, IPAddress ip, string name, CancellationToken cancellationToken = default)
    {
        var body = new { groupId, ip = ip.ToString(), name };
        return PostAsync<FirewallProxyOpenResponse>(baseUrl, "Api/AtlasFirewall/open", apiKey, body, cancellationToken);
    }

    public Task<FirewallProxyUsageResponse> ReportUsedAsync(string baseUrl, string apiKey, string groupId, IPAddress ip, CancellationToken cancellationToken = default)
    {
        var body = new { groupId, ip = ip.ToString() };
        return PostAsync<FirewallProxyUsageResponse>(baseUrl, "Api/AtlasFirewall/used", apiKey, body, cancellationToken);
    }

    private async Task<T> PostAsync<T>(string baseUrl, string path, string apiKey, object body, CancellationToken cancellationToken)
    {
        var uri = new Uri(new Uri(string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl), path);
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-API-KEY", apiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new Quilt4NetFirewallAuthorizationException(
                $"Quilt4Net firewall proxy returned {(int)response.StatusCode} {response.StatusCode}. The key may be revoked, lack the required firewall scope, or target a group it is not bound to.");
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}

internal sealed record FirewallProxyOpenResponse
{
    public string Outcome { get; init; } // "Opened" | "AlreadyOpen" | "Failed"
}

internal sealed record FirewallProxyUsageResponse
{
    public string Outcome { get; init; } // "Recorded" | "RecordedNoCredential"
}

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Atlas;
using Tharga.MongoDB.Configuration;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class Quilt4NetFirewallServiceTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage LastRequest { get; private set; }
        public string LastBody { get; private set; }
        public Func<HttpRequestMessage, HttpResponseMessage> Reply { get; init; } = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { outcome = "Opened" })
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content != null) LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return Reply(request);
        }
    }

    private static (Quilt4NetFirewallService sut, CapturingHandler handler) Build(Func<HttpRequestMessage, HttpResponseMessage> reply = null)
    {
        var handler = new CapturingHandler();
        if (reply != null) handler = new CapturingHandler { Reply = reply };

        var services = new ServiceCollection();
        services.AddHttpClient(Quilt4NetFirewallProxyClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => handler);
        var sp = services.BuildServiceProvider();

        var proxy = new Quilt4NetFirewallProxyClient(sp.GetRequiredService<IHttpClientFactory>());
        return (new Quilt4NetFirewallService(proxy), handler);
    }

    private static MongoDbApiAccess NewAccess(string baseUrl = "https://q4n.test/") => new()
    {
        GroupId = "g1",
        Quilt4NetApiKey = "key1",
        Quilt4NetBaseUrl = baseUrl,
    };

    [Fact]
    public async Task OpenAsync_SendsXApiKey_PostToOpenEndpoint_WithGroupIdAndIp()
    {
        var (sut, handler) = Build();

        await sut.OpenAsync(NewAccess(), IPAddress.Parse("203.0.113.4"), name: "test-host");

        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri.ToString().Should().Be("https://q4n.test/Api/AtlasFirewall/open");
        handler.LastRequest.Headers.GetValues("X-API-KEY").Single().Should().Be("key1");
        handler.LastBody.Should().Contain("\"groupId\":\"g1\"");
        handler.LastBody.Should().Contain("\"ip\":\"203.0.113.4\"");
        handler.LastBody.Should().Contain("\"name\":\"test-host\"");
    }

    [Fact]
    public async Task ReportUsedAsync_PostToUsedEndpoint_NoNameInBody()
    {
        var (sut, handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { outcome = "Recorded" })
        });

        await sut.ReportUsedAsync(NewAccess(), IPAddress.Parse("203.0.113.5"));

        handler.LastRequest.RequestUri.ToString().Should().Be("https://q4n.test/Api/AtlasFirewall/used");
        handler.LastBody.Should().Contain("\"groupId\":\"g1\"");
        handler.LastBody.Should().Contain("\"ip\":\"203.0.113.5\"");
        handler.LastBody.Should().NotContain("\"name\"");
    }

    [Fact]
    public async Task OpenAsync_UsesDefaultBaseUrl_WhenAccessHasNone()
    {
        var (sut, handler) = Build();

        await sut.OpenAsync(new MongoDbApiAccess { GroupId = "g1", Quilt4NetApiKey = "k1" }, IPAddress.Parse("203.0.113.6"));

        handler.LastRequest.RequestUri.ToString().Should().StartWith(Quilt4NetFirewallProxyClient.DefaultBaseUrl);
    }

    [Fact]
    public async Task Unauthorized_ThrowsQuilt4NetFirewallAuthorizationException()
    {
        var (sut, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var act = () => sut.OpenAsync(NewAccess(), IPAddress.Parse("203.0.113.7"));

        await act.Should().ThrowAsync<Quilt4NetFirewallAuthorizationException>();
    }

    [Fact]
    public async Task Forbidden_ThrowsQuilt4NetFirewallAuthorizationException()
    {
        var (sut, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var act = () => sut.ReportUsedAsync(NewAccess(), IPAddress.Parse("203.0.113.8"));

        await act.Should().ThrowAsync<Quilt4NetFirewallAuthorizationException>();
    }
}

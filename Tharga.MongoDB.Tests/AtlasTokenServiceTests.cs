using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Atlas;
using Tharga.MongoDB.Configuration;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class AtlasTokenServiceTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Func<HttpRequestMessage, HttpResponseMessage> Reply { get; init; } = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { access_token = "tok", token_type = "Bearer", expires_in = 3600 })
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Reply(request));
        }
    }

    private static (AtlasTokenService sut, CountingHandler handler) Build(Func<HttpRequestMessage, HttpResponseMessage> reply = null)
    {
        var handler = reply == null ? new CountingHandler() : new CountingHandler { Reply = reply };

        var services = new ServiceCollection();
        services.AddHttpClient(AtlasTokenService.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => handler);
        var sp = services.BuildServiceProvider();

        return (new AtlasTokenService(sp.GetRequiredService<IHttpClientFactory>()), handler);
    }

    private static MongoDbApiAccess NewAccess(string clientId = "cid") => new()
    {
        ClientId = clientId,
        ClientSecret = "csecret",
        GroupId = "g1",
    };

    [Fact]
    public async Task GetAccessTokenAsync_Returns_Token_OnSuccess()
    {
        var (sut, _) = Build();

        var token = await sut.GetAccessTokenAsync(NewAccess());

        token.Should().Be("tok");
    }

    [Fact]
    public async Task GetAccessTokenAsync_ServesFromCache_OnSecondCall()
    {
        var (sut, handler) = Build();
        var access = NewAccess(Guid.NewGuid().ToString());

        await sut.GetAccessTokenAsync(access);
        await sut.GetAccessTokenAsync(access);

        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAccessTokenAsync_Throws_WithLikelyExpiredTrue_When401BodyMentionsExpire()
    {
        var (sut, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":\"the client secret has expired\"}")
        });

        var act = () => sut.GetAccessTokenAsync(NewAccess(Guid.NewGuid().ToString()));

        var ex = (await act.Should().ThrowAsync<AtlasServiceAccountAuthException>()).Which;
        ex.LikelyExpired.Should().BeTrue();
        ex.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAccessTokenAsync_Throws_WithLikelyExpiredFalse_When401BodyWithoutExpire()
    {
        var (sut, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":\"invalid client credentials\"}")
        });

        var act = () => sut.GetAccessTokenAsync(NewAccess(Guid.NewGuid().ToString()));

        var ex = (await act.Should().ThrowAsync<AtlasServiceAccountAuthException>()).Which;
        ex.LikelyExpired.Should().BeFalse();
        ex.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

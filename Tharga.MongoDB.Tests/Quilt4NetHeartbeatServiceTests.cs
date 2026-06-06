using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Atlas;
using Tharga.MongoDB.Configuration;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class Quilt4NetHeartbeatServiceTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        public ConcurrentBag<string> RequestPaths { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Reply { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { outcome = "Recorded" })
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestPaths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
            return Task.FromResult(Reply(request));
        }
    }

    private static (Quilt4NetHeartbeatService sut, CountingHandler handler) Build(TimeSpan? interval = null)
    {
        interval ??= TimeSpan.FromMilliseconds(80);

        var handler = new CountingHandler();
        var services = new ServiceCollection();
        services.AddHttpClient(Quilt4NetFirewallProxyClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => handler);
        var sp = services.BuildServiceProvider();

        var proxy = new Quilt4NetFirewallProxyClient(sp.GetRequiredService<IHttpClientFactory>());
        var firewall = new Quilt4NetFirewallService(proxy);
        var opts = Options.Create(new DatabaseOptions { Quilt4NetHeartbeatInterval = interval });
        var sut = new Quilt4NetHeartbeatService(firewall, opts, NullLogger<Quilt4NetHeartbeatService>.Instance);
        return (sut, handler);
    }

    private static MongoDbApiAccess NewAccess(string suffix) => new()
    {
        PublicKey = "p" + suffix,
        PrivateKey = "k" + suffix,
        GroupId = "g" + suffix,
        Quilt4NetApiKey = "q" + suffix,
        Quilt4NetBaseUrl = "https://q4n.test/",
    };

    [Fact]
    public void Register_OnlyNotifyOrOpen_IgnoresClassicAndNone()
    {
        var (sut, _) = Build();
        var access = NewAccess("1");
        var ip = IPAddress.Parse("203.0.113.1");

        sut.Register(access, ip, FirewallMode.Classic);
        sut.Register(access, ip, FirewallMode.None);

        sut.ActiveCount.Should().Be(0);

        sut.Register(access, ip, FirewallMode.Notify);
        sut.ActiveCount.Should().Be(1);
    }

    [Fact]
    public void Unregister_RemovesEntry()
    {
        var (sut, _) = Build();
        var access = NewAccess("1");
        var ip = IPAddress.Parse("203.0.113.2");

        sut.Register(access, ip, FirewallMode.Open);
        sut.ActiveCount.Should().Be(1);

        sut.Unregister(access, ip);
        sut.ActiveCount.Should().Be(0);
    }

    [Fact]
    public async Task NotifyMode_TicksReportUsed()
    {
        var (sut, handler) = Build(TimeSpan.FromMilliseconds(40));
        sut.Register(NewAccess("1"), IPAddress.Parse("203.0.113.3"), FirewallMode.Notify);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await sut.StopAsync(CancellationToken.None);

        handler.RequestPaths.Should().Contain(p => p.EndsWith("/Api/AtlasFirewall/used"));
        handler.RequestPaths.Should().NotContain(p => p.EndsWith("/Api/AtlasFirewall/open"));
    }

    [Fact]
    public async Task OpenMode_TicksOpenAsync_NotReportUsed()
    {
        var (sut, handler) = Build(TimeSpan.FromMilliseconds(40));
        handler.Reply = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { outcome = "AlreadyOpen" })
        };

        sut.Register(NewAccess("2"), IPAddress.Parse("203.0.113.4"), FirewallMode.Open);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await sut.StopAsync(CancellationToken.None);

        handler.RequestPaths.Should().Contain(p => p.EndsWith("/Api/AtlasFirewall/open"));
        handler.RequestPaths.Should().NotContain(p => p.EndsWith("/Api/AtlasFirewall/used"));
    }

    [Fact]
    public async Task EmptyActive_DormantTick_NoHttpCalls()
    {
        var (sut, handler) = Build(TimeSpan.FromMilliseconds(40));

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await sut.StopAsync(CancellationToken.None);

        handler.RequestPaths.Should().BeEmpty();
    }

    [Fact]
    public async Task AuthRejected_RemovesEntry()
    {
        var (sut, handler) = Build(TimeSpan.FromMilliseconds(40));
        handler.Reply = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);

        sut.Register(NewAccess("3"), IPAddress.Parse("203.0.113.5"), FirewallMode.Notify);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await sut.StopAsync(CancellationToken.None);

        sut.ActiveCount.Should().Be(0, "auth-rejected entries should be removed from the loop");
    }

    [Fact]
    public async Task TransientError_KeepsEntry()
    {
        var (sut, handler) = Build(TimeSpan.FromMilliseconds(40));
        handler.Reply = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        sut.Register(NewAccess("4"), IPAddress.Parse("203.0.113.6"), FirewallMode.Notify);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await sut.StopAsync(CancellationToken.None);

        sut.ActiveCount.Should().Be(1, "transient errors should keep the entry so the next tick retries");
    }
}

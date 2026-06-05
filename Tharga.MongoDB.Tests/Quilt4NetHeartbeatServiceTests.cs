using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Quilt4Net.Toolkit.Features.Atlas;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Atlas;
using Tharga.MongoDB.Configuration;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class Quilt4NetHeartbeatServiceTests
{
    private static (Quilt4NetHeartbeatService sut, Mock<IAtlasFirewallClient> client) Build(TimeSpan? interval = null)
    {
        interval ??= TimeSpan.FromMilliseconds(80);
        var client = new Mock<IAtlasFirewallClient>();
        client.Setup(c => c.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new FirewallOpenResult());
        client.Setup(c => c.ReportUsedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new FirewallUsageResult());

        var factory = new Mock<IAtlasFirewallClientFactory>();
        factory.Setup(f => f.Create(It.IsAny<Quilt4Net.Toolkit.Features.ValueGroup.AtlasFirewallProxyKeyEntry>()))
               .Returns(client.Object);

        var firewall = new Quilt4NetFirewallService(factory.Object);
        var opts = Options.Create(new DatabaseOptions { Quilt4NetHeartbeatInterval = interval });
        var sut = new Quilt4NetHeartbeatService(firewall, opts, NullLogger<Quilt4NetHeartbeatService>.Instance);
        return (sut, client);
    }

    private static MongoDbApiAccess NewAccess(string suffix) => new()
    {
        PublicKey = "p" + suffix,
        PrivateKey = "k" + suffix,
        GroupId = "g" + suffix,
        Quilt4NetApiKey = "q" + suffix,
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
        var (sut, client) = Build(TimeSpan.FromMilliseconds(40));
        sut.Register(NewAccess("1"), IPAddress.Parse("203.0.113.3"), FirewallMode.Notify);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await sut.StopAsync(CancellationToken.None);

        client.Verify(c => c.ReportUsedAsync("203.0.113.3", It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        client.Verify(c => c.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OpenMode_TicksOpenAsync_NotReportUsed()
    {
        var (sut, client) = Build(TimeSpan.FromMilliseconds(40));
        sut.Register(NewAccess("2"), IPAddress.Parse("203.0.113.4"), FirewallMode.Open);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await sut.StopAsync(CancellationToken.None);

        client.Verify(c => c.OpenAsync("203.0.113.4", null, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        client.Verify(c => c.ReportUsedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmptyActive_DormantTick_NoCallsToFactory()
    {
        var (sut, client) = Build(TimeSpan.FromMilliseconds(40));

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await sut.StopAsync(CancellationToken.None);

        client.Verify(c => c.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(c => c.ReportUsedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AuthRejected_RemovesEntry()
    {
        var (sut, client) = Build(TimeSpan.FromMilliseconds(40));
        client.Setup(c => c.ReportUsedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new AtlasFirewallAuthorizationException("revoked"));

        sut.Register(NewAccess("3"), IPAddress.Parse("203.0.113.5"), FirewallMode.Notify);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await sut.StopAsync(CancellationToken.None);

        sut.ActiveCount.Should().Be(0, "auth-rejected entries should be removed from the loop");
    }

    [Fact]
    public async Task TransientError_KeepsEntry()
    {
        var (sut, client) = Build(TimeSpan.FromMilliseconds(40));
        client.Setup(c => c.ReportUsedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new System.Net.Http.HttpRequestException("transient"));

        sut.Register(NewAccess("4"), IPAddress.Parse("203.0.113.6"), FirewallMode.Notify);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await sut.StopAsync(CancellationToken.None);

        sut.ActiveCount.Should().Be(1, "transient errors should keep the entry so the next tick retries");
    }
}

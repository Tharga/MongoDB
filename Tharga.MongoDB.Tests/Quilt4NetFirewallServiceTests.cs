using FluentAssertions;
using Moq;
using Quilt4Net.Toolkit.Features.Atlas;
using Quilt4Net.Toolkit.Features.ValueGroup;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Atlas;
using Tharga.MongoDB.Configuration;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class Quilt4NetFirewallServiceTests
{
    private static (Quilt4NetFirewallService sut, Mock<IAtlasFirewallClientFactory> factory, Mock<IAtlasFirewallClient> client)
        Build()
    {
        var client = new Mock<IAtlasFirewallClient>();
        var factory = new Mock<IAtlasFirewallClientFactory>();
        factory.Setup(f => f.Create(It.IsAny<AtlasFirewallProxyKeyEntry>())).Returns(client.Object);
        return (new Quilt4NetFirewallService(factory.Object), factory, client);
    }

    [Fact]
    public async Task OpenAsync_SendsManageEntry_AndCallsOpenWithIp()
    {
        var (sut, factory, client) = Build();
        client.Setup(c => c.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new FirewallOpenResult());

        var access = new MongoDbApiAccess { GroupId = "g1", Quilt4NetApiKey = "k1" };

        await sut.OpenAsync(access, IPAddress.Parse("203.0.113.4"));

        factory.Verify(f => f.Create(It.Is<AtlasFirewallProxyKeyEntry>(e =>
            e.ApiKey == "k1" && e.GroupId == "g1" && e.CanManage == true)), Times.Once);
        client.Verify(c => c.OpenAsync("203.0.113.4", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReportUsedAsync_SendsUsageEntry_AndCallsReportUsedWithIp()
    {
        var (sut, factory, client) = Build();
        client.Setup(c => c.ReportUsedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new FirewallUsageResult());

        var access = new MongoDbApiAccess { GroupId = "g1", Quilt4NetApiKey = "k2" };

        await sut.ReportUsedAsync(access, IPAddress.Parse("203.0.113.5"));

        factory.Verify(f => f.Create(It.Is<AtlasFirewallProxyKeyEntry>(e =>
            e.ApiKey == "k2" && e.GroupId == "g1" && e.CanManage == false)), Times.Once);
        client.Verify(c => c.ReportUsedAsync("203.0.113.5", It.IsAny<CancellationToken>()), Times.Once);
    }
}

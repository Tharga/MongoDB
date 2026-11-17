using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using AwesomeAssertions;
using Moq;
using Tharga.MongoDB.Atlas;
using Tharga.MongoDB.Configuration;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Coverage for GitHub issue #200. The access list was searched with CidrBlock.StartsWith(ip), so 52.1.2.34/32 counted as 52.1.2.3.
/// </summary>
public class MongoDbFirewallServiceTests
{
    private static readonly MongoDbApiAccess Access = new() { GroupId = "group", PublicKey = "public", PrivateKey = "private" };

    private sealed class FakeFirewallService : MongoDbFirewallService
    {
        private readonly string[] _cidrBlocks;

        public FakeFirewallService(string externalIp, params string[] cidrBlocks)
            : base(Mock.Of<IExternalIpAddressService>(x => x.GetExternalIpAddressAsync() == Task.FromResult(IPAddress.Parse(externalIp))), null, null)
        {
            _cidrBlocks = cidrBlocks;
        }

        public List<IPAddress> Added { get; } = [];

        public override async IAsyncEnumerable<WhiteListItem> GetFirewallListAsync(MongoDbApiAccess access)
        {
            await Task.CompletedTask;
            foreach (var cidr in _cidrBlocks) yield return new WhiteListItem { CidrBlock = cidr, Comment = "other" };
        }

        public override Task AddToFirewallAsync(MongoDbApiAccess access, string name, IPAddress ipAddress)
        {
            Added.Add(ipAddress);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AssureFirewallAccess_IpIsPrefixOfAnotherEntry_AddsTheIp()
    {
        var sut = new FakeFirewallService("52.1.2.3", "52.1.2.34/32");

        var result = await sut.AssureFirewallAccessAsync(Access);

        result.Result.Should().Be(EFirewallOpenResult.Open);
        sut.Added.Should().ContainSingle().Which.ToString().Should().Be("52.1.2.3");
    }

    [Fact]
    public async Task HaveAccess_IpIsPrefixOfAnotherEntry_HasNoAccess()
    {
        var sut = new FakeFirewallService("52.1.2.3", "52.1.2.34/32");

        var (haveAccess, _) = await sut.HaveAccess(Access);

        haveAccess.Should().BeFalse();
    }

    [Fact]
    public async Task AssureFirewallAccess_ExactEntry_IsAlreadyOpen()
    {
        var sut = new FakeFirewallService("52.1.2.3", "52.1.2.34/32", "52.1.2.3/32");

        var result = await sut.AssureFirewallAccessAsync(Access);

        result.Result.Should().Be(EFirewallOpenResult.AlreadyOpen);
        sut.Added.Should().BeEmpty();
    }

    [Theory]
    [InlineData("52.1.2.0/24", true)]
    [InlineData("52.1.0.0/16", true)]
    [InlineData("0.0.0.0/0", true)]
    [InlineData("52.1.3.0/24", false)]
    [InlineData("52.1.2.3", true)]
    [InlineData("52.1.2.4/32", false)]
    public async Task HaveAccess_WiderBlocks_AreTestedForContainment(string cidr, bool expected)
    {
        var sut = new FakeFirewallService("52.1.2.3", cidr);

        var (haveAccess, _) = await sut.HaveAccess(Access);

        haveAccess.Should().Be(expected);
    }
}

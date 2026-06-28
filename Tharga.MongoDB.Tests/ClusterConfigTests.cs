using FluentAssertions;
using Tharga.MongoDB;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class ClusterConfigTests
{
    [Theory]
    [InlineData("M0", 500)]
    [InlineData("M10", 1500)]
    [InlineData("M30", 3000)]
    [InlineData("M50", 16000)]
    public void AtlasTier_LimitFor_KnownTiers(string tier, int expected)
        => AtlasTier.LimitFor(tier).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("M999")]
    [InlineData("nonsense")]
    public void AtlasTier_LimitFor_UnknownOrEmpty_IsNull(string tier)
        => AtlasTier.LimitFor(tier).Should().BeNull();

    [Fact]
    public void EffectiveLimit_TierWinsOverManualLimit()
    {
        var entry = new ClusterConfigEntry { Cluster = "c", Tier = "M30", Limit = 99 };
        entry.EffectiveLimit.Should().Be(3000);
    }

    [Fact]
    public void EffectiveLimit_FallsBackToManualLimit_WhenNoTier()
    {
        var entry = new ClusterConfigEntry { Cluster = "c", Tier = null, Limit = 250 };
        entry.EffectiveLimit.Should().Be(250);
    }

    [Fact]
    public void EffectiveLimit_IsNull_WhenNeitherSet()
    {
        var entry = new ClusterConfigEntry { Cluster = "c" };
        entry.EffectiveLimit.Should().BeNull();
    }

    [Theory]
    [InlineData("cluster0.ab12.mongodb.net:27017", true)]
    [InlineData("shard.mongodbgov.net:27017", true)]
    [InlineData("localhost:27017", false)]
    [InlineData("127.0.0.1:27017", false)]
    [InlineData("", false)]
    public void MongoDbCluster_IsAtlas_DetectsAtlasHosts(string cluster, bool expected)
        => MongoDbCluster.IsAtlas(cluster).Should().Be(expected);
}

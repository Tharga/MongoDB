using FluentAssertions;
using Tharga.MongoDB.Configuration;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class FirewallModeTests
{
    [Fact]
    public void GetFirewallMode_None_WhenNull()
    {
        MongoDbApiAccess access = null;
        access.GetFirewallMode().Should().Be(FirewallMode.None);
    }

    [Fact]
    public void GetFirewallMode_None_WhenAllFieldsEmpty()
    {
        new MongoDbApiAccess().GetFirewallMode().Should().Be(FirewallMode.None);
    }

    [Fact]
    public void GetFirewallMode_Classic_WhenOnlyAtlasKeys()
    {
        new MongoDbApiAccess
        {
            PublicKey = "pub",
            PrivateKey = "priv",
            GroupId = "g1",
        }.GetFirewallMode().Should().Be(FirewallMode.Classic);
    }

    [Fact]
    public void GetFirewallMode_Notify_WhenBothAtlasAndQuilt4NetKeys()
    {
        new MongoDbApiAccess
        {
            PublicKey = "pub",
            PrivateKey = "priv",
            GroupId = "g1",
            Quilt4NetApiKey = "q4n",
        }.GetFirewallMode().Should().Be(FirewallMode.Notify);
    }

    [Fact]
    public void GetFirewallMode_Open_WhenOnlyQuilt4NetKey()
    {
        new MongoDbApiAccess
        {
            GroupId = "g1",
            Quilt4NetApiKey = "q4n",
        }.GetFirewallMode().Should().Be(FirewallMode.Open);
    }

    [Fact]
    public void GetFirewallMode_None_WhenQuilt4NetKeyButNoGroupId()
    {
        // GroupId is required for Quilt4Net (it's the Atlas project the key is bound to).
        new MongoDbApiAccess
        {
            Quilt4NetApiKey = "q4n",
        }.GetFirewallMode().Should().Be(FirewallMode.None);
    }

    [Fact]
    public void GetFirewallMode_Classic_WhenAtlasComplete_AndQuilt4NetKeyMissing()
    {
        new MongoDbApiAccess
        {
            PublicKey = "pub",
            PrivateKey = "priv",
            GroupId = "g1",
            Quilt4NetBaseUrl = "https://quilt4net.local/",
            // No Quilt4NetApiKey — base URL alone doesn't promote to Notify.
        }.GetFirewallMode().Should().Be(FirewallMode.Classic);
    }

    [Fact]
    public void HasFirewallConfiguration_False_WhenNull()
    {
        MongoDbApiAccess access = null;
        access.HasFirewallConfiguration().Should().BeFalse();
    }

    [Fact]
    public void HasFirewallConfiguration_False_WhenAllFieldsEmpty()
    {
        new MongoDbApiAccess().HasFirewallConfiguration().Should().BeFalse();
    }

    [Fact]
    public void HasFirewallConfiguration_True_WhenOnlyAtlasKeys()
    {
        new MongoDbApiAccess
        {
            PublicKey = "pub",
            PrivateKey = "priv",
            GroupId = "g1",
        }.HasFirewallConfiguration().Should().BeTrue();
    }

    [Fact]
    public void HasFirewallConfiguration_True_WhenBothAtlasAndQuilt4NetKeys()
    {
        new MongoDbApiAccess
        {
            PublicKey = "pub",
            PrivateKey = "priv",
            GroupId = "g1",
            Quilt4NetApiKey = "q4n",
        }.HasFirewallConfiguration().Should().BeTrue();
    }

    [Fact]
    public void HasFirewallConfiguration_True_WhenOnlyQuilt4NetKey()
    {
        // Open mode: Quilt4Net key + GroupId, no Atlas keys. HasMongoDbApiAccess would return false here, which is why the startup firewall open used to skip Open-mode configs.
        new MongoDbApiAccess
        {
            GroupId = "g1",
            Quilt4NetApiKey = "q4n",
        }.HasFirewallConfiguration().Should().BeTrue();
    }

    [Fact]
    public void HasFirewallConfiguration_False_WhenQuilt4NetKeyButNoGroupId()
    {
        new MongoDbApiAccess
        {
            Quilt4NetApiKey = "q4n",
        }.HasFirewallConfiguration().Should().BeFalse();
    }
}

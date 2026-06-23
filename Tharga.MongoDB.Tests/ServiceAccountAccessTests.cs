using FluentAssertions;
using Tharga.MongoDB.Configuration;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class ServiceAccountAccessTests
{
    [Fact]
    public void GetFirewallMode_Classic_WhenOnlyServiceAccountKeys()
    {
        new MongoDbApiAccess
        {
            ClientId = "cid",
            ClientSecret = "csecret",
            GroupId = "g1",
        }.GetFirewallMode().Should().Be(FirewallMode.Classic);
    }

    [Fact]
    public void GetFirewallMode_Notify_WhenServiceAccountAndQuilt4NetKeys()
    {
        new MongoDbApiAccess
        {
            ClientId = "cid",
            ClientSecret = "csecret",
            GroupId = "g1",
            Quilt4NetApiKey = "q4n",
        }.GetFirewallMode().Should().Be(FirewallMode.Notify);
    }

    [Fact]
    public void HasMongoDbApiAccess_True_WhenServiceAccountAndGroupId()
    {
        new MongoDbApiAccess
        {
            ClientId = "cid",
            ClientSecret = "csecret",
            GroupId = "g1",
        }.HasMongoDbApiAccess().Should().BeTrue();
    }

    [Fact]
    public void HasMongoDbApiAccess_False_WhenServiceAccountWithoutGroupId()
    {
        new MongoDbApiAccess
        {
            ClientId = "cid",
            ClientSecret = "csecret",
        }.HasMongoDbApiAccess().Should().BeFalse();
    }

    [Fact]
    public void HasMongoDbApiAccess_True_WhenDigestAndGroupId()
    {
        new MongoDbApiAccess
        {
            PublicKey = "pub",
            PrivateKey = "priv",
            GroupId = "g1",
        }.HasMongoDbApiAccess().Should().BeTrue();
    }

    [Fact]
    public void HasFirewallConfiguration_True_WhenServiceAccountAndGroupId()
    {
        new MongoDbApiAccess
        {
            ClientId = "cid",
            ClientSecret = "csecret",
            GroupId = "g1",
        }.HasFirewallConfiguration().Should().BeTrue();
    }

    [Fact]
    public void UsesServiceAccount_True_WhenBothFieldsSet()
    {
        new MongoDbApiAccess
        {
            ClientId = "cid",
            ClientSecret = "csecret",
        }.UsesServiceAccount().Should().BeTrue();
    }

    [Fact]
    public void UsesServiceAccount_False_WhenClientSecretMissing()
    {
        new MongoDbApiAccess
        {
            ClientId = "cid",
        }.UsesServiceAccount().Should().BeFalse();
    }

    [Fact]
    public void UsesServiceAccount_False_WhenClientIdMissing()
    {
        new MongoDbApiAccess
        {
            ClientSecret = "csecret",
        }.UsesServiceAccount().Should().BeFalse();
    }

    [Fact]
    public void UsesServiceAccount_False_WhenNull()
    {
        MongoDbApiAccess access = null;
        access.UsesServiceAccount().Should().BeFalse();
    }
}

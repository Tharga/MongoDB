using AwesomeAssertions;
using MongoDB.Driver;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class MongoDbClientProviderCacheKeyTests
{
    [Theory]
    [InlineData("mongodb://reader:secret1@localhost:27017/Db", "mongodb://writer:secret2@localhost:27017/Db")]
    [InlineData("mongodb://user:secret1@localhost:27017/Db", "mongodb://user:secret2@localhost:27017/Db")]
    [InlineData("mongodb://user:secret@localhost:27017/Db?authSource=admin", "mongodb://user:secret@localhost:27017/Db?authSource=other")]
    [InlineData("mongodb://localhost:27017/Db", "mongodb://localhost:27017/Db?tls=true")]
    [InlineData("mongodb://localhost:27017/Db", "mongodb://localhost:27017/Db?readPreference=secondary")]
    public void GetClient_SameClusterDifferentClientSettings_ReturnsSeparateClients(string first, string second)
    {
        var sut = new MongoDbClientProvider();

        var a = sut.GetClient(new MongoUrl(first));
        var b = sut.GetClient(new MongoUrl(second));

        a.Should().NotBeSameAs(b);
    }

    [Fact]
    public void GetClient_DifferentUsers_EachClientUsesItsOwnUser()
    {
        var sut = new MongoDbClientProvider();

        var reader = sut.GetClient(new MongoUrl("mongodb://reader:secret1@localhost:27017/Db"));
        var writer = sut.GetClient(new MongoUrl("mongodb://writer:secret2@localhost:27017/Db"));

        reader.Settings.Credential.Username.Should().Be("reader");
        writer.Settings.Credential.Username.Should().Be("writer");
    }

    [Fact]
    public void GetClient_SameSettingsDifferentDatabase_SharesOneClient()
    {
        var sut = new MongoDbClientProvider();

        var a = sut.GetClient(new MongoUrl("mongodb://user:secret@localhost:27017/Aggregator?authSource=admin&maxPoolSize=200"));
        var b = sut.GetClient(new MongoUrl("mongodb://user:secret@localhost:27017/Integration?authSource=admin&maxPoolSize=200"));

        a.Should().BeSameAs(b);
    }

    [Fact]
    public void GetClientKey_DoesNotContainThePassword()
    {
        var key = MongoDbClientProvider.GetClientKey(new MongoUrl("mongodb://user:VerySecretPassword@localhost:27017/Db"));

        key.Should().NotContain("VerySecretPassword");
        key.Should().NotContain("user");
    }

    [Fact]
    public void GetServerKey_DifferentUsers_StaysOneClusterKey()
    {
        // The execute limiter and the monitor still group by cluster and pool size.
        var a = MongoDbClientProvider.GetServerKey(new MongoUrl("mongodb://reader:secret1@localhost:27017/Db"));
        var b = MongoDbClientProvider.GetServerKey(new MongoUrl("mongodb://writer:secret2@localhost:27017/Db"));

        a.Should().Be(b);
    }
}

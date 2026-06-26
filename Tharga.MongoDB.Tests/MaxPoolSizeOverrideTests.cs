using System;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Driver;
using Moq;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class MaxPoolSizeOverrideTests
{
    // --- Cache key (fixes #126) ---

    [Fact]
    public void GetServerKey_SameHost_DifferentMaxPoolSize_ProducesDifferentKeys()
    {
        var low = MongoDbClientProvider.GetServerKey(new MongoUrl("mongodb://localhost:27017/Db?maxPoolSize=100"));
        var high = MongoDbClientProvider.GetServerKey(new MongoUrl("mongodb://localhost:27017/Db?maxPoolSize=300"));

        low.Should().NotBe(high);
    }

    [Fact]
    public void GetServerKey_SameHost_SameMaxPoolSize_ProducesSameKey()
    {
        // Different database, same cluster + same pool size -> still one shared client.
        var a = MongoDbClientProvider.GetServerKey(new MongoUrl("mongodb://localhost:27017/Aggregator?maxPoolSize=200"));
        var b = MongoDbClientProvider.GetServerKey(new MongoUrl("mongodb://localhost:27017/Integration?maxPoolSize=200"));

        a.Should().Be(b);
    }

    // --- Override delegate (#128) ---

    private static MongoUrlBuilderLoader CreateLoader(DatabaseOptions options, IServiceProvider provider = null)
        => new(provider ?? Mock.Of<IServiceProvider>(), options);

    private static Func<MongoUrl, MongoUrl> GetApplicator(DatabaseOptions options, IServiceProvider provider = null)
        => CreateLoader(options, provider).GetConnectionStringBuilder(null).ApplyPoolSizeOverride;

    [Fact]
    public void Override_Upserts_MaxConnectionPoolSize_FromConnectionStringValue()
    {
        var options = new DatabaseOptions
        {
            DefaultConfigurationName = "Aggregator",
            MaxPoolSizeOverride = (_, name, _) => Task.FromResult(name == "Aggregator" ? 500 : 50),
        };

        var result = GetApplicator(options)(new MongoUrl("mongodb://localhost:27017/Db?maxPoolSize=100"));

        result.MaxConnectionPoolSize.Should().Be(500);
    }

    [Fact]
    public void Override_ReceivesDriverDefault_WhenConnectionStringHasNoMaxPoolSize()
    {
        var seen = 0;
        var options = new DatabaseOptions
        {
            DefaultConfigurationName = "Aggregator",
            MaxPoolSizeOverride = (_, _, current) => { seen = current; return Task.FromResult(current); },
        };

        GetApplicator(options)(new MongoUrl("mongodb://localhost:27017/Db"));

        seen.Should().Be(100); // MongoDB driver default
    }

    [Fact]
    public void Override_PassesServiceProviderAndConfigurationName()
    {
        var provider = Mock.Of<IServiceProvider>();
        IServiceProvider seenProvider = null;
        string seenName = null;
        var options = new DatabaseOptions
        {
            DefaultConfigurationName = "Integration",
            MaxPoolSizeOverride = (sp, name, current) => { seenProvider = sp; seenName = name; return Task.FromResult(current); },
        };

        GetApplicator(options, provider)(new MongoUrl("mongodb://localhost:27017/Db?maxPoolSize=100"));

        seenProvider.Should().BeSameAs(provider);
        seenName.Should().Be("Integration");
    }

    [Fact]
    public void NoOverride_LeavesUrlUnchanged()
    {
        var options = new DatabaseOptions { DefaultConfigurationName = "Aggregator" };
        var url = new MongoUrl("mongodb://localhost:27017/Db?maxPoolSize=100");

        var result = GetApplicator(options)(url);

        result.Should().BeSameAs(url);
    }

    [Fact]
    public void Override_ReturningSameValue_LeavesUrlUnchanged()
    {
        var options = new DatabaseOptions
        {
            DefaultConfigurationName = "Aggregator",
            MaxPoolSizeOverride = (_, _, current) => Task.FromResult(current),
        };
        var url = new MongoUrl("mongodb://localhost:27017/Db?maxPoolSize=150");

        var result = GetApplicator(options)(url);

        result.Should().BeSameAs(url);
    }
}

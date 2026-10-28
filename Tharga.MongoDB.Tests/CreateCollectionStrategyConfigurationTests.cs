using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// The strategy resolves like the other configuration values: loader by name, loader in general, appsettings by
/// name, appsettings in general, then the default. It used to be a non-nullable enum, so an unset value read as
/// <c>DropEmpty</c> and stopped the cascade at the first configuration object found (#189).
/// </summary>
public class CreateCollectionStrategyConfigurationTests
{
    private static MongoDbConfig Resolve(Dictionary<string, string> appSettings = null, MongoDbConfigurationTree loaded = null)
    {
        var builder = new ConfigurationBuilder();
        if (appSettings != null) builder.AddInMemoryCollection(appSettings);
        var provider = new ServiceCollection().AddSingleton<IConfiguration>(builder.Build()).BuildServiceProvider();
        var options = new DatabaseOptions { ConfigurationLoader = loaded == null ? null : _ => Task.FromResult(loaded) };

        return new RepositoryConfiguration(provider, options).GetConfiguration();
    }

    [Fact]
    public void NothingConfigured_DefaultsToCreateOnAdd()
    {
        Resolve().CreateCollectionStrategy.Should().Be(CreateStrategy.CreateOnAdd);
    }

    [Fact]
    public void GeneralAppSettings_Applies()
    {
        var result = Resolve(new Dictionary<string, string> { { "MongoDB:CreateCollectionStrategy", "CreateOnGet" } });

        result.CreateCollectionStrategy.Should().Be(CreateStrategy.CreateOnGet);
    }

    [Fact]
    public void NamedAppSettings_OverridesGeneral()
    {
        var result = Resolve(new Dictionary<string, string>
        {
            { "MongoDB:CreateCollectionStrategy", "CreateOnAdd" },
            { "MongoDB:Default:CreateCollectionStrategy", "CreateOnGet" },
        });

        result.CreateCollectionStrategy.Should().Be(CreateStrategy.CreateOnGet);
    }

    [Fact]
    public void Loader_OverridesAppSettings()
    {
        var loaded = new MongoDbConfigurationTree
        {
            Configurations = new Dictionary<ConfigurationName, MongoDbConfiguration>
            {
                { "Default", new MongoDbConfiguration { CreateCollectionStrategy = CreateStrategy.CreateOnGet } }
            }
        };

        var result = Resolve(new Dictionary<string, string> { { "MongoDB:CreateCollectionStrategy", "CreateOnAdd" } }, loaded);

        result.CreateCollectionStrategy.Should().Be(CreateStrategy.CreateOnGet);
    }

    [Fact]
    public void LoaderWithoutStrategy_FallsThroughToAppSettings()
    {
        var loaded = new MongoDbConfigurationTree
        {
            Configurations = new Dictionary<ConfigurationName, MongoDbConfiguration>
            {
                { "Default", new MongoDbConfiguration { FetchSize = 10 } }
            }
        };

        var result = Resolve(new Dictionary<string, string> { { "MongoDB:CreateCollectionStrategy", "CreateOnGet" } }, loaded);

        result.CreateCollectionStrategy.Should().Be(CreateStrategy.CreateOnGet);
    }
}

using System;
using System.Collections.Concurrent;
using MongoDB.Driver;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Internals;

internal class RepositoryConfigurationInternal : IRepositoryConfigurationInternal
{
    private readonly IMongoUrlBuilderLoader _mongoUrlBuilderLoader;
    private readonly IRepositoryConfiguration _repositoryConfiguration;
    private readonly DatabaseOptions _databaseOptions;
    private readonly string _environmentName;
    private readonly Lazy<DatabaseContext> _databaseContext;

    private static readonly ConcurrentDictionary<string, MongoDbConfig> _configurationCache = new();
    private static readonly ConcurrentDictionary<string, MongoUrl> _databaseUrlCache = new();

    public RepositoryConfigurationInternal(IMongoUrlBuilderLoader mongoUrlBuilderLoader, IRepositoryConfiguration repositoryConfiguration, DatabaseOptions databaseOptions, Func<DatabaseContext> databaseContextLoader, string environmentName = null)
    {
        _mongoUrlBuilderLoader = mongoUrlBuilderLoader;
        _repositoryConfiguration = repositoryConfiguration;
        _databaseOptions = databaseOptions;
        _environmentName = environmentName;
        _databaseContext = new Lazy<DatabaseContext>(() => databaseContextLoader?.Invoke());
    }

    public MongoUrl GetDatabaseUrl()
    {
        var configurationName = _databaseContext.Value?.ConfigurationName ?? _databaseOptions.ConfigurationName ?? "Default";
        var key = $"{_environmentName}.{configurationName}.{_databaseContext.Value?.CollectionName}.{_databaseContext.Value?.DatabasePart}";
        if (_databaseUrlCache.TryGetValue(key, out var mongoUrl)) return mongoUrl;

        var result = _mongoUrlBuilderLoader.GetConnectionStringBuilder(_databaseContext.Value);
        mongoUrl = result.Builder.Build(result.ConnectionStringLoader(), _databaseContext.Value?.DatabasePart);
        _databaseUrlCache.TryAdd(key, mongoUrl);
        return mongoUrl;
    }

    public MongoDbConfig GetConfiguration()
    {
        var configurationName = _databaseContext.Value?.ConfigurationName ?? _databaseOptions.ConfigurationName ?? "Default";
        var key = $"{configurationName}.{_databaseContext.Value?.CollectionName}";
        if (_configurationCache.TryGetValue(key, out var configuration)) return configuration;

        configuration = _repositoryConfiguration.GetConfiguration(configurationName);
        _configurationCache.TryAdd(key, configuration);
        return configuration;
    }
}
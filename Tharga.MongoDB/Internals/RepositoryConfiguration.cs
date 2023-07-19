using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Internals;

internal class RepositoryConfiguration : IRepositoryConfiguration
{
    private readonly IConfiguration _configuration;
    private readonly IMongoUrlBuilderLoader _mongoUrlBuilderLoader;
    private readonly MongoDbConfigurationTree _mongoDbConfiguration;
    private readonly string _environmentName;
    private readonly Lazy<DatabaseContext> _databaseContext;

    private static readonly ConcurrentDictionary<string, MongoDbConfig> ConfigurationCache = new();
    private static readonly ConcurrentDictionary<string, MongoUrl> DatabaseUrlCache = new();

    public RepositoryConfiguration(IConfiguration configuration, IMongoUrlBuilderLoader mongoUrlBuilderLoader, MongoDbConfigurationTree mongoDbConfiguration, Func<DatabaseContext> databaseContextLoader, string environmentName = null)
    {
        _configuration = configuration;
        _mongoUrlBuilderLoader = mongoUrlBuilderLoader;
        _mongoDbConfiguration = mongoDbConfiguration;
        _environmentName = environmentName;
        _databaseContext = new Lazy<DatabaseContext>(() => databaseContextLoader?.Invoke());
    }

    public MongoUrl GetDatabaseUrl()
    {
        var key = $"{_environmentName}.{_databaseContext.Value?.ConfigurationName}.{_databaseContext.Value?.CollectionName}.{_databaseContext.Value?.DatabasePart}";
        if (DatabaseUrlCache.TryGetValue(key, out var mongoUrl)) return mongoUrl;

        var result = _mongoUrlBuilderLoader.GetConnectionStringBuilder(_databaseContext.Value);
        mongoUrl = result.Builder.Build(result.ConnectionStringLoader(), _databaseContext.Value?.DatabasePart);
        DatabaseUrlCache.TryAdd(key, mongoUrl);
        return mongoUrl;
    }

    public MongoDbConfig GetConfiguration()
    {
        var key = $"{_databaseContext.Value?.ConfigurationName}.{_databaseContext.Value?.CollectionName}";
        if (ConfigurationCache.TryGetValue(key, out var configuration)) return configuration;

        //Provided as named parameter
        MongoDbConfiguration c1 = null;
        _mongoDbConfiguration?.Configurations?.TryGetValue(_databaseContext.Value?.ConfigurationName ?? string.Empty, out c1);

        //Provided as general parameter
        var c2 = _mongoDbConfiguration as MongoDbConfiguration;

        //Configured as named parameter
        var c3 = GetConfigValue<MongoDbConfiguration>($"MongoDB:{_databaseContext.Value?.ConfigurationName}");

        //Configured as general parameter
        var c4 = GetConfigValue<MongoDbConfiguration>("MongoDB");

        configuration = new MongoDbConfig
        {
            AccessInfo = c1?.AccessInfo ?? c2?.AccessInfo ?? c3?.AccessInfo ?? c4?.AccessInfo,
            ResultLimit = c1?.ResultLimit ?? c2?.ResultLimit ?? c3?.ResultLimit ?? c4?.ResultLimit,
            AutoClean = c1?.AutoClean ?? c2?.AutoClean ?? c3?.AutoClean ?? c4?.AutoClean ?? true,
            CleanOnStartup = c1?.CleanOnStartup ?? c2?.CleanOnStartup ?? c3?.CleanOnStartup ?? c4?.CleanOnStartup ?? false,
            DropEmptyCollections = c1?.DropEmptyCollections ?? c2?.DropEmptyCollections ?? c3?.DropEmptyCollections ?? c4?.DropEmptyCollections ?? true
        };

        ConfigurationCache.TryAdd(key, configuration);
        return configuration;
    }

    private T GetConfigValue<T>(string path) where T : new()
    {
        var obj = new T();
        _configuration?.GetSection(path).Bind(obj);
        return obj;
    }
}
using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
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

    public ConfigurationName GetConfigurationName()
    {
        var configurationName = _databaseContext.Value?.ConfigurationName?.Value ?? _databaseOptions.DefaultConfigurationName ?? throw new NullReferenceException("No default configuration name provided.");
        return configurationName;
    }

    public DatabaseContext GetDatabaseContext()
    {
        return _databaseContext.Value;
    }

    public MongoUrl GetDatabaseUrl()
    {
        var databaseName = (_databaseContext.Value as DatabaseContextWithFingerprint)?.DatabaseName;
        if (databaseName != null)
        {
            var result = _mongoUrlBuilderLoader.GetConnectionStringBuilder(_databaseContext.Value);

            var mongoUrl = result.Builder.Build(result.ConnectionStringLoader(), null);

            var server = mongoUrl.ToString().TrimEnd(mongoUrl.DatabaseName);
            mongoUrl = new MongoUrl($"{server}{databaseName}");
            return mongoUrl;
        }
        else
        {
            var configurationName = GetConfigurationName();
            var key = $"{_environmentName}.{configurationName}.{_databaseContext.Value?.CollectionName}.{_databaseContext.Value?.DatabasePart}";
            if (_databaseUrlCache.TryGetValue(key, out var mongoUrl)) return mongoUrl;

            var result = _mongoUrlBuilderLoader.GetConnectionStringBuilder(_databaseContext.Value);

            mongoUrl = result.Builder.Build(result.ConnectionStringLoader(), _databaseContext.Value?.DatabasePart);

            _databaseUrlCache.TryAdd(key, mongoUrl);
            return mongoUrl;
        }
    }

    public MongoDbConfig GetConfiguration()
    {
        var configurationName = GetConfigurationName();
        var key = $"{configurationName}.{_databaseContext.Value?.CollectionName}";
        if (_configurationCache.TryGetValue(key, out var configuration)) return configuration;

        configuration = _repositoryConfiguration.GetConfiguration(configurationName);
        _configurationCache.TryAdd(key, configuration);
        return configuration;
    }

    public LogLevel GetExecuteInfoLogLevel()
    {
        var logLevel = _databaseOptions.ExecuteInfoLogLevel ?? LogLevel.Debug;
        return logLevel;
    }

    public bool ShouldAssureIndex() => _databaseOptions?.AssureIndex ?? true;
}
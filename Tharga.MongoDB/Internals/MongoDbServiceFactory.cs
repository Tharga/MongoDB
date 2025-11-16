using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Bson.Serialization;
using Tharga.MongoDB.Atlas;

namespace Tharga.MongoDB.Internals;

internal class MongoDbServiceFactory : IMongoDbServiceFactory
{
    private readonly IRepositoryConfigurationLoader _repositoryConfigurationLoader;
    private readonly IMongoDbFirewallStateService _mongoDbFirewallStateService;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, MongoDbService> _databaseDbServices = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public MongoDbServiceFactory(IRepositoryConfigurationLoader repositoryConfigurationLoader, IMongoDbFirewallStateService mongoDbFirewallStateService, ILogger<MongoDbServiceFactory> logger)
    {
        _repositoryConfigurationLoader = repositoryConfigurationLoader;
        _mongoDbFirewallStateService = mongoDbFirewallStateService;
        _logger = logger;
    }

    public event EventHandler<ConfigurationAccessEventArgs> ConfigurationAccessEvent;
    public event EventHandler<CollectionAccessEventArgs> CollectionAccessEvent;

    public IMongoDbService GetMongoDbService(Func<DatabaseContext> databaseContextLoader)
    {
        try
        {
            BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.CSharpLegacy));
        }
        catch (BsonSerializationException)
        {
        }

        var configuration = _repositoryConfigurationLoader.GetConfiguration(databaseContextLoader);
        var configurationName = configuration.GetConfigurationName();
        var mongoUrl = configuration.GetDatabaseUrl();
        var cacheKey = mongoUrl.Url;
        ConfigurationAccessEvent?.Invoke(this, new ConfigurationAccessEventArgs(configurationName, mongoUrl));
        if (_databaseDbServices.TryGetValue(cacheKey, out var dbService)) return dbService;

        try
        {
            _lock.Wait();
            if (_databaseDbServices.TryGetValue(cacheKey, out dbService)) return dbService;

            dbService = new MongoDbService(configuration, _mongoDbFirewallStateService, _logger);
            dbService.CollectionAccessEvent += (s, e) =>
            {
                CollectionAccessEvent?.Invoke(s, e);
            };
            _databaseDbServices.TryAdd(cacheKey, dbService);
            return dbService;
        }
        finally
        {
            _lock.Release();
        }
    }
}
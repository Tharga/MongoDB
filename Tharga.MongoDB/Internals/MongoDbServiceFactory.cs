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
    private readonly IDatabaseMonitor _databaseMonitor;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, MongoDbService> _databaseDbServices = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public MongoDbServiceFactory(IRepositoryConfigurationLoader repositoryConfigurationLoader, IMongoDbFirewallStateService mongoDbFirewallStateService, IDatabaseMonitor databaseMonitor, ILogger<MongoDbServiceFactory> logger)
    {
        _repositoryConfigurationLoader = repositoryConfigurationLoader;
        _mongoDbFirewallStateService = mongoDbFirewallStateService;
        _databaseMonitor = databaseMonitor;
        _logger = logger;
    }

    public IMongoDbService GetMongoDbService(Func<DatabaseContext> databaseContextLoader)
    {
        try
        {
            BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.CSharpLegacy));
        }
        catch (BsonSerializationException)
        {
        }

        var mongoUrl = _repositoryConfigurationLoader.GetConfiguration(databaseContextLoader).GetDatabaseUrl();
        var cacheKey = mongoUrl.Url;
        if (_databaseDbServices.TryGetValue(cacheKey, out var dbService)) return dbService;

        try
        {
            _lock.Wait();
            if (_databaseDbServices.TryGetValue(cacheKey, out dbService)) return dbService;

            dbService = new MongoDbService(_repositoryConfigurationLoader.GetConfiguration(databaseContextLoader), _mongoDbFirewallStateService, _databaseMonitor, _logger);
            _databaseDbServices.TryAdd(cacheKey, dbService);
            return dbService;
        }
        finally
        {
            _lock.Release();
        }
    }
}
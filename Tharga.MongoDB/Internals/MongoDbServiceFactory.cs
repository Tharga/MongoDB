using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;
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

    public IMongoDbService GetMongoDbService(Func<DatabaseContext> databaseContextLoader)
    {
        var mongoUrl = _repositoryConfigurationLoader.GetConfiguration(databaseContextLoader).GetDatabaseUrl();
        if (_databaseDbServices.TryGetValue(mongoUrl.DatabaseName, out var dbService)) return dbService;

        try
        {
            _lock.Wait();
            if (_databaseDbServices.TryGetValue(mongoUrl.DatabaseName, out dbService)) return dbService;

            dbService = new MongoDbService(_repositoryConfigurationLoader.GetConfiguration(databaseContextLoader), _mongoDbFirewallStateService, _logger);
            _databaseDbServices.TryAdd(mongoUrl.DatabaseName, dbService);
            return dbService;
        }
        finally
        {
            _lock.Release();
        }
    }
}
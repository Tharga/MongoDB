using System;
using Microsoft.Extensions.Logging;
using Tharga.MongoDB.Atlas;

namespace Tharga.MongoDB.Internals;

internal class MongoDbServiceFactory : IMongoDbServiceFactory
{
    private readonly IRepositoryConfigurationLoader _repositoryConfigurationLoader;
    private readonly IMongoDbFirewallService _mongoDbFirewallService;
    private readonly ILogger _logger;

    public MongoDbServiceFactory(IRepositoryConfigurationLoader repositoryConfigurationLoader, IMongoDbFirewallService mongoDbFirewallService, ILogger<MongoDbServiceFactory> logger)
    {
        _repositoryConfigurationLoader = repositoryConfigurationLoader;
        _mongoDbFirewallService = mongoDbFirewallService;
        _logger = logger;
    }

    public IMongoDbService GetMongoDbService(Func<DatabaseContext> databaseContextLoader)
    {
        return new MongoDbService(_repositoryConfigurationLoader.GetConfiguration(databaseContextLoader), _mongoDbFirewallService, _logger);
    }

    public IMongoDbService GetMongoDbService(string databasePart)
    {
        return new MongoDbService(_repositoryConfigurationLoader.GetConfiguration(() => new DatabaseContext { DatabasePart = databasePart }), _mongoDbFirewallService, _logger);
    }

    public IMongoDbService GetMongoDbService()
    {
        return new MongoDbService(_repositoryConfigurationLoader.GetConfiguration(null), _mongoDbFirewallService, _logger);
    }
}
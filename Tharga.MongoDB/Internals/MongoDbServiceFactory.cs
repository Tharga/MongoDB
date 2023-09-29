using System;
using Microsoft.Extensions.Logging;
using Tharga.MongoDB.Atlas;

namespace Tharga.MongoDB.Internals;

internal class MongoDbServiceFactory : IMongoDbServiceFactory
{
    private readonly IRepositoryConfigurationLoader _repositoryConfigurationLoader;
    private readonly IMongoDbFirewallStateService _mongoDbFirewallStateService;
    private readonly ILogger _logger;

    public MongoDbServiceFactory(IRepositoryConfigurationLoader repositoryConfigurationLoader, IMongoDbFirewallStateService mongoDbFirewallStateService, ILogger<MongoDbServiceFactory> logger)
    {
        _repositoryConfigurationLoader = repositoryConfigurationLoader;
        _mongoDbFirewallStateService = mongoDbFirewallStateService;
        _logger = logger;
    }

    public IMongoDbService GetMongoDbService(Func<DatabaseContext> databaseContextLoader)
    {
        //TODO: This should possibly be a single instance per configuration, or?
        return new MongoDbService(_repositoryConfigurationLoader.GetConfiguration(databaseContextLoader), _mongoDbFirewallStateService, _logger);
    }
}
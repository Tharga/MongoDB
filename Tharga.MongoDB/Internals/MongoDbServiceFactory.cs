using System;
using Microsoft.Extensions.Logging;

namespace Tharga.MongoDB.Internals;

internal class MongoDbServiceFactory : IMongoDbServiceFactory
{
    private readonly IRepositoryConfigurationLoader _repositoryConfigurationLoader;
    private readonly ILogger _logger;

    public MongoDbServiceFactory(IRepositoryConfigurationLoader repositoryConfigurationLoader, ILogger<MongoDbServiceFactory> logger)
    {
        _repositoryConfigurationLoader = repositoryConfigurationLoader;
        _logger = logger;
    }

    public IMongoDbService GetMongoDbService(Func<DatabaseContext> databaseContextLoader)
    {
        return new MongoDbService(_repositoryConfigurationLoader.GetConfiguration(databaseContextLoader), _logger);
    }

    public IMongoDbService GetMongoDbService(string databasePart)
    {
        return new MongoDbService(_repositoryConfigurationLoader.GetConfiguration(() => new DatabaseContext { DatabasePart = databasePart }), _logger);
    }

    public IMongoDbService GetMongoDbService()
    {
        return new MongoDbService(_repositoryConfigurationLoader.GetConfiguration(null), _logger);
    }
}
using System;

namespace Tharga.MongoDB.Internals;

internal class MongoDbServiceFactory : IMongoDbServiceFactory
{
    private readonly IRepositoryConfigurationLoader _repositoryConfigurationLoader;

    public MongoDbServiceFactory(IRepositoryConfigurationLoader repositoryConfigurationLoader)
    {
        _repositoryConfigurationLoader = repositoryConfigurationLoader;
    }

    public IMongoDbService GetMongoDbService(Func<DatabaseContext> databaseContextLoader)
    {
        return new MongoDbService(_repositoryConfigurationLoader.GetConfiguration(databaseContextLoader));
    }

    public IMongoDbService GetMongoDbService(string databasePart)
    {
        return new MongoDbService(_repositoryConfigurationLoader.GetConfiguration(() => new DatabaseContext { DatabasePart = databasePart }));
    }

    public IMongoDbService GetMongoDbService()
    {
        return new MongoDbService(_repositoryConfigurationLoader.GetConfiguration(null));
    }
}
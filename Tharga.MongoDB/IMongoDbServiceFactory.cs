using System;

namespace Tharga.MongoDB;

public interface IMongoDbServiceFactory
{
    event EventHandler<ConfigurationAccessEventArgs> ConfigurationAccessEvent;
    event EventHandler<CollectionAccessEventArgs> CollectionAccessEvent;

    IMongoDbService GetMongoDbService(Func<DatabaseContext> databaseContextLoader);
}
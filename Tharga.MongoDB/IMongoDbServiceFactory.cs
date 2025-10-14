using System;

namespace Tharga.MongoDB;

public interface IMongoDbServiceFactory
{
    event EventHandler<CollectionAccessEventArgs> CollectionAccessEvent;

    IMongoDbService GetMongoDbService(Func<DatabaseContext> databaseContextLoader);
}
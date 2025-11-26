using System;

namespace Tharga.MongoDB;

public interface IMongoDbServiceFactory
{
    event EventHandler<ConfigurationAccessEventArgs> ConfigurationAccessEvent;
    event EventHandler<CollectionAccessEventArgs> CollectionAccessEvent;
    event EventHandler<CallStartEventArgs> CallStartEvent;
    event EventHandler<CallEndEventArgs> CallEndEvent;

    IMongoDbService GetMongoDbService(Func<DatabaseContext> databaseContextLoader);
}
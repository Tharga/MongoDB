using System;

namespace Tharga.MongoDB;

public interface IMongoDbServiceFactory
{
    event EventHandler<IndexUpdatedEventArgs> IndexUpdatedEvent;
    event EventHandler<CollectionAccessEventArgs> CollectionAccessEvent;
    event EventHandler<CallStartEventArgs> CallStartEvent;
    event EventHandler<CallEndEventArgs> CallEndEvent;

    IMongoDbService GetMongoDbService(Func<DatabaseContext> databaseContextLoader);
}
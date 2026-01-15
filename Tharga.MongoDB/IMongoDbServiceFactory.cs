using System;

namespace Tharga.MongoDB;

public interface IMongoDbServiceFactory
{
    event EventHandler<CollectionAccessEventArgs> CollectionAccessEvent;
    event EventHandler<IndexUpdatedEventArgs> IndexUpdatedEvent;
    event EventHandler<CollectionDroppedEventArgs> CollectionDroppedEvent;
    event EventHandler<CallStartEventArgs> CallStartEvent;
    event EventHandler<CallEndEventArgs> CallEndEvent;

    IMongoDbService GetMongoDbService(Func<DatabaseContext> databaseContextLoader);
}
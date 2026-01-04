using System;

namespace Tharga.MongoDB;

public interface IMongoDbServiceFactory
{
    event EventHandler<IndexUpdatedEventArgs> IndexUpdatedEvent;
    event EventHandler<CollectionAccessEventArgs> CollectionAccessEvent;
    event EventHandler<CallStartEventArgs> CallStartEvent;
    event EventHandler<CallEndEventArgs> CallEndEvent;
    event EventHandler<ExecuteInfoChangedEventArgs> ExecuteInfoChangedEvent;

    IMongoDbService GetMongoDbService(Func<DatabaseContext> databaseContextLoader);
}
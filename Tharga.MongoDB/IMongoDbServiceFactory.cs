using System;

namespace Tharga.MongoDB;

public interface IMongoDbServiceFactory
{
    event EventHandler<CollectionAccessEventArgs> CollectionAccessEvent;
    event EventHandler<IndexUpdatedEventArgs> IndexUpdatedEvent;
    event EventHandler<CollectionDroppedEventArgs> CollectionDroppedEvent;
    event EventHandler<CallStartEventArgs> CallStartEvent;
    event EventHandler<CallEndEventArgs> CallEndEvent;

    string SourceName { get; }

    /// <summary>
    /// Resolved value of <c>DatabaseOptions.AllowDelayedCommit</c>. Surfaced on the factory
    /// so <see cref="Lockable.LockableRepositoryCollectionBase{TEntity,TKey}"/> can read it as
    /// the default for its virtual <c>AllowDelayedCommit</c> property without taking a direct
    /// dependency on <c>IOptions&lt;DatabaseOptions&gt;</c>.
    /// </summary>
    bool AllowDelayedCommit { get; }

    IMongoDbService GetMongoDbService(Func<DatabaseContext> databaseContextLoader);
}
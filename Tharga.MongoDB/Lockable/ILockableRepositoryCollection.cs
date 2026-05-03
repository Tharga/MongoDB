using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Tharga.MongoDB.Lockable;

public interface ILockableRepositoryCollection<TEntity, TKey> : IRepositoryCollection<TEntity, TKey>
    where TEntity : LockableEntityBase<TKey>
{
    event EventHandler<LockEventArgs<TEntity>> LockEvent;

    //Update
    Task<long> UpdateUnlockedAsync(Expression<Func<TEntity, bool>> predicate, UpdateDefinition<TEntity> update);
    Task<long> UpdateUnlockedAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update);

    //Delete
    Task<TEntity> DeleteOneUnlockedAsync(Expression<Func<TEntity, bool>> predicate, OneOption<TEntity> options = null);
    Task<TEntity> DeleteOneUnlockedAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null);

    Task<long> DeleteManyUnlockedAsync(Expression<Func<TEntity, bool>> predicate = null);
    Task<long> DeleteManyUnlockedAsync(FilterDefinition<TEntity> filter);
    Task<long> DeleteManyAsync(DeleteMode deleteMode, Expression<Func<TEntity, bool>> predicate = null);

    //Lock
    Expression<Func<TEntity, bool>> UnlockedOrExpiredFilter { get; }
    Expression<Func<TEntity, bool>> LockedOrExceptionFilter { get; }
    Expression<Func<TEntity, bool>> ExceptionFilter { get; }
    Expression<Func<TEntity, bool>> LockedFilter { get; }

    IAsyncEnumerable<TEntity> GetUnlockedAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<TEntity> GetUnlockedAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    Task<EntityScope<TEntity, TKey>> PickForUpdateAsync(TKey id, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null);
    Task<EntityScope<TEntity, TKey>> PickForUpdateAsync(FilterDefinition<TEntity> filter, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null);
    Task<EntityScope<TEntity, TKey>> PickForUpdateAsync(Expression<Func<TEntity, bool>> predicate = null, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null);
    Task<EntityScope<TEntity, TKey>> PickForDeleteAsync(TKey id, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null);
    Task<EntityScope<TEntity, TKey>> PickForDeleteAsync(FilterDefinition<TEntity> filter, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null);
    Task<EntityScope<TEntity, TKey>> PickForDeleteAsync(Expression<Func<TEntity, bool>> predicate = null, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null);

    Task<EntityScope<TEntity, TKey>> WaitForUpdateAsync(TKey id, TimeSpan? lockTimeout = null, TimeSpan? waitTimeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null, CancellationToken cancellationToken = default);
    Task<EntityScope<TEntity, TKey>> WaitForDeleteAsync(TKey id, TimeSpan? lockTimeout = null, TimeSpan? waitTimeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks a single document without pre-committing to update-vs-delete. The decision is taken at commit time
    /// via <see cref="LockScope{TEntity, TKey}.CommitAsync(CommitMode, TEntity)"/>.
    /// </summary>
    Task<LockScope<TEntity, TKey>> LockAsync(TKey id, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null);

    /// <summary>
    /// Locks a single document matched by <paramref name="filter"/> without pre-committing to update-vs-delete.
    /// </summary>
    Task<LockScope<TEntity, TKey>> LockAsync(FilterDefinition<TEntity> filter, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null);

    /// <summary>
    /// Locks a single document matched by <paramref name="predicate"/> without pre-committing to update-vs-delete.
    /// </summary>
    Task<LockScope<TEntity, TKey>> LockAsync(Expression<Func<TEntity, bool>> predicate = null, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null);

    /// <summary>
    /// Locks multiple documents identified by <paramref name="ids"/>. Acquisition is sequential and ordered by key
    /// to avoid AB / BA deadlocks; if any acquisition fails, locks acquired so far are released and the failure
    /// is propagated (the lease never returns half-acquired). Per-document commit decisions are staged on the
    /// returned <see cref="DocumentLease{TEntity, TKey}"/>.
    /// </summary>
    Task<DocumentLease<TEntity, TKey>> LockManyAsync(IEnumerable<TKey> ids, TimeSpan? timeout = null, string actor = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks all documents matched by <paramref name="filter"/>. The filter is resolved to an id list at acquire time;
    /// documents added later are not locked. Otherwise behaves like <see cref="LockManyAsync(IEnumerable{TKey}, TimeSpan?, string, CancellationToken)"/>.
    /// </summary>
    Task<DocumentLease<TEntity, TKey>> LockManyAsync(FilterDefinition<TEntity> filter, TimeSpan? timeout = null, string actor = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks all documents matched by <paramref name="predicate"/>. The predicate is resolved to an id list at acquire time.
    /// </summary>
    Task<DocumentLease<TEntity, TKey>> LockManyAsync(Expression<Func<TEntity, bool>> predicate, TimeSpan? timeout = null, string actor = null, CancellationToken cancellationToken = default);

    IAsyncEnumerable<EntityLock<TEntity, TKey>> GetWithLockInfoAsync(FilterDefinition<TEntity> filter = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<EntityLock<TEntity, TKey>> GetLockedAsync(LockMode lockMode, FilterDefinition<TEntity> filter = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<EntityLock<TEntity, TKey>> GetExpiredAsync(FilterDefinition<TEntity> filter = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    Task<EntityChangeResult<TEntity>> ReleaseOneAsync(TKey id, ReleaseMode mode);
    Task<long> ReleaseManyAsync(ReleaseMode mode);
}

public interface ILockableRepositoryCollection<TEntity> : ILockableRepositoryCollection<TEntity, ObjectId>
    where TEntity : LockableEntityBase<ObjectId>
{
}
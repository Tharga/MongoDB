using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB.Lockable;

public class LockEventArgs<TEntity> : EventArgs
{
    public LockEventArgs(TEntity entity, LockAction lockAction)
    {
        Entity = entity;
        LockAction = lockAction;
    }

    public TEntity Entity { get; }
    public LockAction LockAction { get; }
}

public enum LockAction
{
    Locked,
    Abandoned,
    CommitUpdated,
    CommitDeleted,
    Exception
}

public interface ILockableRepositoryCollection<TEntity, TKey> : IReadOnlyRepositoryCollection<TEntity, TKey>
    where TEntity : LockableEntityBase<TKey>
{
    event EventHandler<LockEventArgs<TEntity>> LockEvent;

    //Create
    Task AddAsync(TEntity entity);
    Task<bool> TryAddAsync(TEntity entity);

    //Update
    Task<long> UpdateAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update);

    //Delete
    Task<TEntity> DeleteOneAsync(TKey id);
    Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate, OneOption<TEntity> options = null);
    Task<TEntity> DeleteOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null);
    Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate);
    Task<long> DeleteManyAsync(FilterDefinition<TEntity> filter);
    Task<long> DeleteManyAsync(DeleteMode deleteMode, Expression<Func<TEntity, bool>> predicate = null);

    //Other
    Task<CollectionScope<TEntity>> GetCollectionScope(Operation operation);
    Task DropCollectionAsync();
    IAsyncEnumerable<TEntity> GetDirtyAsync();
    IEnumerable<(IndexFailOperation Operation, string Name)> GetFailedIndices();

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
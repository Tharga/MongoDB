using System.Threading.Tasks;
using System;
using MongoDB.Bson;
using System.Collections.Generic;
using System.Threading;
using MongoDB.Driver;
using System.Linq.Expressions;

namespace Tharga.MongoDB.Lockable;

public interface ILockableRepositoryCollection<TEntity, TKey> : IReadOnlyRepositoryCollection<TEntity, TKey>
    where TEntity : LockableEntityBase<TKey>
{
    Expression<Func<TEntity, bool>> UnlockedOrExpiredFilter { get; }
    Expression<Func<TEntity, bool>> LockedOrExceptionFilter { get; }
    Expression<Func<TEntity, bool>> ExceptionFilter { get; }
    Expression<Func<TEntity, bool>> LockedFilter { get; }

    IAsyncEnumerable<TEntity> GetUnlockedAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<TEntity> GetUnlockedAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    Task AddAsync(TEntity entity);
    Task<bool> TryAddAsync(TEntity entity);
    Task AddManyAsync(IEnumerable<TEntity> entities);

    Task<EntityScope<TEntity, TKey>> PickForUpdateAsync(TKey id, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null);
    Task<EntityScope<TEntity, TKey>> PickForUpdateAsync(FilterDefinition<TEntity> filter, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null);
    Task<EntityScope<TEntity, TKey>> PickForUpdateAsync(Expression<Func<TEntity, bool>> predicate = null, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null);
    Task<EntityScope<TEntity, TKey>> PickForDeleteAsync(TKey id, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null);
    Task<EntityScope<TEntity, TKey>> PickForDeleteAsync(FilterDefinition<TEntity> filter, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null);
    Task<EntityScope<TEntity, TKey>> PickForDeleteAsync(Expression<Func<TEntity, bool>> predicate = null, TimeSpan? timeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null);

    Task<EntityScope<TEntity, TKey>> WaitForUpdateAsync(TKey id, TimeSpan? lockTimeout = null, TimeSpan? waitTimeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null, CancellationToken cancellationToken = default);
    Task<EntityScope<TEntity, TKey>> WaitForDeleteAsync(TKey id, TimeSpan? lockTimeout = null, TimeSpan? waitTimeout = null, string actor = null, Func<CallbackResult<TEntity>, Task> completeAction = null, CancellationToken cancellationToken = default);

    IAsyncEnumerable<EntityLock<TEntity, TKey>> GetLockedAsync(LockMode lockMode, FilterDefinition<TEntity> filter = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<EntityLock<TEntity, TKey>> GetExpiredAsync(FilterDefinition<TEntity> filter = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    Task<EntityChangeResult<TEntity>> ReleaseOneAsync(TKey id, ReleaseMode mode);
    Task<long> ReleaseManyAsync(ReleaseMode mode);

    Task<long> UpdateAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update);
    Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate);
    Task<long> DeleteManyAsync(DeleteMode deleteMode, Expression<Func<TEntity, bool>> predicate = null);
}

public interface ILockableRepositoryCollection<TEntity> : ILockableRepositoryCollection<TEntity, ObjectId>
    where TEntity : LockableEntityBase<ObjectId>
{
}
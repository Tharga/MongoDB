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

    IAsyncEnumerable<TEntity> GetUnlockedAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<TEntity> GetUnlockedAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    Task AddAsync(TEntity entity);
    Task<bool> TryAddAsync(TEntity entity);
    Task AddManyAsync(IEnumerable<TEntity> entities);

    Task<EntityScope<TEntity, TKey>> PickForUpdateAsync(TKey id, TimeSpan? timeout = default, string actor = default, Func<CallbackResult<TEntity>, Task> completed = default);
    Task<EntityScope<TEntity, TKey>> PickForDeleteAsync(TKey id, TimeSpan? timeout = default, string actor = default, Func<CallbackResult<TEntity>, Task> completed = default);

    Task<EntityScope<TEntity, TKey>> WaitForUpdateAsync(TKey id, TimeSpan? lockTimeout = default, TimeSpan? waitTimeout = default, string actor = default, Func<CallbackResult<TEntity>, Task> completed = default, CancellationToken cancellationToken = default);
    Task<EntityScope<TEntity, TKey>> WaitForDeleteAsync(TKey id, TimeSpan? lockTimeout = default, TimeSpan? waitTimeout = default, string actor = default, Func<CallbackResult<TEntity>, Task> completed = default, CancellationToken cancellationToken = default);

    IAsyncEnumerable<EntityLock<TEntity, TKey>> GetLockedAsync(LockMode lockMode, FilterDefinition<TEntity> filter = default, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<EntityLock<TEntity, TKey>> GetExpiredAsync(FilterDefinition<TEntity> filter = default, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    Task<EntityChangeResult<TEntity>> ReleaseOneAsync(TKey id, ReleaseMode mode);
    Task<bool> ReleaseManyAsync(ReleaseMode mode);

    Task<long> UpdateAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update);
    Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate);
}

public interface ILockableRepositoryCollection<TEntity> : ILockableRepositoryCollection<TEntity, ObjectId>
    where TEntity : LockableEntityBase<ObjectId>
{
}
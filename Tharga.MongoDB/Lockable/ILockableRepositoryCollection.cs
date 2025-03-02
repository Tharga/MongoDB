using System.Threading.Tasks;
using System;
using MongoDB.Bson;
using System.Collections.Generic;
using System.Threading;

namespace Tharga.MongoDB.Lockable;

public interface ILockableRepositoryCollection<TEntity, TKey> : IReadOnlyRepositoryCollection<TEntity, TKey>
    where TEntity : LockableEntityBase<TKey>
{
    Task AddAsync(TEntity entity);
    Task<bool> TryAddAsync(TEntity entity);
    Task AddManyAsync(IEnumerable<TEntity> entities);

    Task<EntityScope<TEntity, TKey>> PickForUpdateAsync(TKey id, TimeSpan? timeout = default, string actor = default);
    Task<EntityScope<TEntity, TKey>> PickForDeleteAsync(TKey id, TimeSpan? timeout = default, string actor = default);

    Task<EntityScope<TEntity, TKey>> WaitForUpdateAsync(TKey id, TimeSpan? timeout = default, string actor = default, CancellationToken cancellationToken = default);
    Task<EntityScope<TEntity, TKey>> WaitForDeleteAsync(TKey id, TimeSpan? timeout = default, string actor = default, CancellationToken cancellationToken = default);

    IAsyncEnumerable<EntityLock<TEntity, TKey>> GetLockedAsync(LockMode lockMode);

    Task<bool> ReleaseAsync(TKey id);
}

public interface ILockableRepositoryCollection<TEntity> : ILockableRepositoryCollection<TEntity, ObjectId>
    where TEntity : LockableEntityBase<ObjectId>
{
}
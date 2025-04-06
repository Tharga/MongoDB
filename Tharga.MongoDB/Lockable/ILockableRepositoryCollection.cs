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
    IAsyncEnumerable<TEntity> GetUnlocked(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    Task AddAsync(TEntity entity);
    Task<bool> TryAddAsync(TEntity entity);
    Task AddManyAsync(IEnumerable<TEntity> entities);

    Task<EntityScope<TEntity, TKey>> PickForUpdateAsync(TKey id, TimeSpan? timeout = default, string actor = default);
    Task<EntityScope<TEntity, TKey>> PickForDeleteAsync(TKey id, TimeSpan? timeout = default, string actor = default);

    Task<EntityScope<TEntity, TKey>> WaitForUpdateAsync(TKey id, TimeSpan? timeout = default, string actor = default, CancellationToken cancellationToken = default);
    Task<EntityScope<TEntity, TKey>> WaitForDeleteAsync(TKey id, TimeSpan? timeout = default, string actor = default, CancellationToken cancellationToken = default);

    IAsyncEnumerable<EntityLock<TEntity, TKey>> GetLockedAsync(LockMode lockMode);

    Task<bool> ReleaseAsync(TKey id, ReleaseMode mode);

    Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate);

    [Obsolete("Do not use this feature. It overrides the lock-protection.")]
    IMongoCollection<TEntity> GetCollection();
}

public interface ILockableRepositoryCollection<TEntity> : ILockableRepositoryCollection<TEntity, ObjectId>
    where TEntity : LockableEntityBase<ObjectId>
{
}
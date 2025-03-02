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

    //TODO: Implement more methods, with lambda entries.
    Task<EntityScope<TEntity, TKey>> PickForUpdateAsync(TKey id, TimeSpan? timeout = default, string actor = default);
    Task<EntityScope<TEntity, TKey>> PickForDeleteAsync(TKey id, TimeSpan? timeout = default, string actor = default);

    Task<EntityScope<TEntity, TKey>> WaitForUpdateAsync(TKey id, TimeSpan? timeout = default, string actor = default, CancellationToken cancellationToken = default);

    //TODO: Pick item for deletion
    //Task<EntityScope<TEntity, TKey>> GetForDeleteAsync(TKey id, TimeSpan? timeout = default, string actor = default);
    //Task<EntityScope<TEntity, TKey>> WaitForDeleteAsync(TKey id, TimeSpan? timeout = default, string actor = default);

    //TODO: List locked documents (Filter on locks, expired locks or exceptions)
    //IAsyncEnumerable<EntityScope<TEntity, TKey>> GetAsync();

    //TODO: Create method to manually unlock errors, and reset the counter.
    //Task<TEntity> ReleaseAsync(TKey id);
}

public interface ILockableRepositoryCollection<TEntity> : ILockableRepositoryCollection<TEntity, ObjectId>
    where TEntity : LockableEntityBase<ObjectId>
{
}
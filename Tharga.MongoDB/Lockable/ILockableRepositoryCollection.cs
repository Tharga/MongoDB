using System.Threading.Tasks;
using System;
using MongoDB.Bson;
using System.Collections.Generic;

namespace Tharga.MongoDB.Lockable;

public interface ILockableRepositoryCollection<TEntity, TKey> : IReadOnlyRepositoryCollection<TEntity, TKey>
    where TEntity : LockableEntityBase<TKey>
{
    Task AddAsync(TEntity entity);
    Task<bool> TryAddAsync(TEntity entity);
    Task AddManyAsync(IEnumerable<TEntity> entities);
    //Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity);
    Task<EntityScope<TEntity, TKey>> GetForUpdateAsync(TKey id, TimeSpan? timeout = default, string actor = default);
}

public interface ILockableRepositoryCollection<TEntity> : ILockableRepositoryCollection<TEntity, ObjectId>
    where TEntity : LockableEntityBase<ObjectId>
{
}
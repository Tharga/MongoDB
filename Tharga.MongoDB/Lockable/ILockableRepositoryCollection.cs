using System.Threading.Tasks;
using System;
using MongoDB.Bson;

namespace Tharga.MongoDB.Lockable;

public interface ILockableRepositoryCollection<TEntity, TKey> : IReadOnlyRepositoryCollection<TEntity, TKey>
    where TEntity : LockableEntityBase<TKey>
{
    Task<EntityScope<TEntity, TKey>> GetForUpdateAsync(TKey id, TimeSpan? timeout = default, string actor = default);
}

public interface ILockableRepositoryCollection<TEntity> : ILockableRepositoryCollection<TEntity, ObjectId>
    where TEntity : LockableEntityBase<ObjectId>
{
}
using MongoDB.Bson;

namespace Tharga.MongoDB.Lockable;

public record EntityLock<T, TKey>
    where T : LockableEntityBase<TKey>
{
    public EntityLock(T entity, Lock l)
    {
        Entity = entity;
        Lock = l;
    }

    public T Entity { get; }
    public Lock Lock { get; }
}

public record EntityLock<T> : EntityLock<T, ObjectId>
    where T : LockableEntityBase<ObjectId>
{
    public EntityLock(T entity, Lock l)
        : base(entity, l)
    {
    }
}
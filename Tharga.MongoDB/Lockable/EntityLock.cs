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
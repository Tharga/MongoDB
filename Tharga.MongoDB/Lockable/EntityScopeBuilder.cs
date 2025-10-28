using System;
using System.Threading.Tasks;
using MongoDB.Bson;

namespace Tharga.MongoDB.Lockable;

public class EntityScopeBuilder
{
    public static EntityScope<T, ObjectId> Build<T>(T entity, Func<T, bool, Exception, Task> releaseAction = null)
        where T : LockableEntityBase<ObjectId>
    {
        return new EntityScope<T, ObjectId>(entity, releaseAction);
    }

    public static EntityScope<T, TKey> Build<T, TKey>(T entity, Func<T, bool, Exception, Task> releaseAction = null)
        where T : LockableEntityBase<TKey>
    {
        return new EntityScope<T, TKey>(entity, releaseAction);
    }
}
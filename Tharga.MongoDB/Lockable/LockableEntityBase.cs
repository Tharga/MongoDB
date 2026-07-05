using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Tharga.MongoDB.Lockable;

public abstract record LockableEntityBase<TKey> : EntityBase<TKey>
{
    [BsonIgnoreIfNull]
    internal Lock Lock { get; init; }
}

public abstract record LockableEntityBase : LockableEntityBase<ObjectId>;

public static class LockableEntityBaseExtensions
{
    public static Lock GetLockInfo(this LockableEntityBase item)
    {
        return item.Lock;
    }

    /// <summary>
    /// Returns a copy of <paramref name="item"/> with its lock state set to <paramref name="lock"/>.
    /// The <see cref="LockableEntityBase{TKey}.Lock"/> property is otherwise managed by the locking
    /// machinery; this is the counterpart to <see cref="GetLockInfo"/> for producing an entity in a
    /// known lock/exception state in memory — for example to unit-test code that reads
    /// <see cref="GetLockInfo"/> / <see cref="Lock.ExceptionInfo"/> without a live database.
    /// </summary>
    /// <typeparam name="T">The concrete lockable entity type.</typeparam>
    /// <param name="item">The entity to copy.</param>
    /// <param name="lock">The lock state to apply (may be <c>null</c> to represent an unlocked entity).</param>
    /// <returns>A new instance of <typeparamref name="T"/> with <see cref="LockableEntityBase{TKey}.Lock"/> set.</returns>
    public static T WithLock<T>(this T item, Lock @lock) where T : LockableEntityBase
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        return item with { Lock = @lock };
    }

    public static FilterDefinition<T> GetDocumentsWithoutExceptionsFilter<T>() where T : LockableEntityBase
    {
        var errorFilter =
            new FilterDefinitionBuilder<T>().Or(
                new FilterDefinitionBuilder<T>().Eq(x => x.Lock, null),
                new FilterDefinitionBuilder<T>().And(
                    new FilterDefinitionBuilder<T>().Ne(x => x.Lock, null),
                    new FilterDefinitionBuilder<T>().Eq(x => x.Lock.ExceptionInfo, null)
                ));

        return errorFilter;
    }
}
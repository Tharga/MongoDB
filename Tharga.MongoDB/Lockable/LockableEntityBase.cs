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
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.MongoDB.Lockable;

public abstract record LockableEntityBase<TKey> : EntityBase<TKey>
{
    [BsonIgnoreIfNull]
    internal Lock Lock { get; init; }

    //[BsonIgnoreIfDefault]
    //internal int UnlockCounter { get; init; }
}

public abstract record LockableEntityBase : LockableEntityBase<ObjectId>;
using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.MongoDB.Lockable;

public abstract record LockableEntityBase<TKey> : EntityBase<TKey>
{
    [BsonIgnoreIfNull]
    public Lock Lock { get; internal init; }

    [BsonIgnoreIfDefault]
    public int UnlockCounter { get; internal init; }
}
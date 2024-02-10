using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.MongoDB.Compress;

public abstract record CompressEntityBase<TEntity, TKey> : EntityBase<TKey>
    where TEntity : EntityBase<TKey>
{
    [BsonElement("AggregateKey")]
    public abstract string AggregateKey { get; }
    public abstract TEntity Merge(TEntity other);
}
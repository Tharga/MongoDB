using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.MongoDB;

[Serializable]
[BsonDiscriminator(Required = true)]
public record EntityBase<TKey> : PersistableEntityBase, IEntity<TKey>
{
    public TKey Id { get; init; }
}

public record EntityBase : EntityBase<ObjectId>
{
}
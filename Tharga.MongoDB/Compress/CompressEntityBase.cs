//using System;
//using MongoDB.Bson;
//using MongoDB.Bson.Serialization.Attributes;

//namespace Tharga.MongoDB.Compress;

//public abstract record CompressEntityBase<TEntity, TKey> : EntityBase<TKey>
//    where TEntity : EntityBase<TKey>
//{
//    public DateTime? Timestamp { get; init; }

//    [BsonElement(nameof(AggregateKey))]
//    public abstract string AggregateKey { get; }

//    [BsonElement(nameof(Granularity))]
//    [BsonRepresentation(BsonType.Int32)]
//    public CompressGranularity Granularity { get; internal init; }

//    public abstract TEntity Merge(TEntity other);
//}
using System;
using MongoDB.Bson;

namespace Tharga.MongoDB;

public abstract record TimeEntityBase
{
    public BsonDocument Id { get; init; }
    public DateTime Time { get; init; }
}
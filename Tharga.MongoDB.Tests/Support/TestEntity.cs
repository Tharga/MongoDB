using MongoDB.Bson;
using Tharga.MongoDB.Lockable;

namespace Tharga.MongoDB.Tests.Support;

public record LockableTestEntity : LockableEntityBase<ObjectId>
{
}

public record TestEntity : EntityBase<ObjectId>
{
    public string Value { get; set; }
    public string ExtraValue { get; set; }
}
using MongoDB.Bson;
using Tharga.MongoDB.Lockable;

namespace Tharga.MongoDB.Tests.Support;

public record LockableTestEntity : LockableEntityBase<ObjectId>
{
    public int Count { get; init; }
}
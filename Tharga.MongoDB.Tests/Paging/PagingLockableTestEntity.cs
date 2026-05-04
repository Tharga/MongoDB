using MongoDB.Bson;
using Tharga.MongoDB.Lockable;

namespace Tharga.MongoDB.Tests.Paging;

public record PagingLockableTestEntity : LockableEntityBase<ObjectId>
{
    public string Name { get; init; }
    public int Bucket { get; init; }
}

using MongoDB.Bson;
using Tharga.MongoDB.Lockable;

namespace HostSample.Features.LockableRepo;

public record MyLockableEntity : LockableEntityBase<ObjectId>
{
    public int Counter { get; set; }
}
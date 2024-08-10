using MongoDB.Bson;
using Tharga.MongoDB.Lockable;

namespace HostSample.Features.LockableRepo;

public interface IMyLockableCollection : ILockableRepositoryCollection<MyLockableEntity, ObjectId>
{
}
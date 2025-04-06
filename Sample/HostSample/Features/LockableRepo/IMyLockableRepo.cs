using MongoDB.Bson;
using Tharga.MongoDB;
using Tharga.MongoDB.Lockable;

namespace HostSample.Features.LockableRepo;

public interface IMyLockableRepo : IRepository
{
    Task AddAsync(MyLockableEntity myLockableEntity);
    IAsyncEnumerable<MyLockableEntity> GetAll();
    IAsyncEnumerable<MyLockableEntity> GetUnlockedAsync();
    Task<MyLockableEntity> BumpCountAsync(ObjectId id);
    Task ThrowAsync(ObjectId id);
    Task LockAsync(ObjectId id, TimeSpan timeout, string actor);
    Task<bool> UnlockAsync(ObjectId id);
    Task<long> DeleteAllAsync();
    IAsyncEnumerable<EntityLock<MyLockableEntity, ObjectId>> GetLockedAsync(LockMode mode);
}
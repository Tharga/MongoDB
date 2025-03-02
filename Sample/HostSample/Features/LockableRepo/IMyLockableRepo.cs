using MongoDB.Bson;
using Tharga.MongoDB;

namespace HostSample.Features.LockableRepo;

public interface IMyLockableRepo : IRepository
{
    Task AddAsync(MyLockableEntity myLockableEntity);
    IAsyncEnumerable<MyLockableEntity> GetAll();
    Task<MyLockableEntity> BumpCountAsync(ObjectId id);
    Task LockAsync(ObjectId id, TimeSpan timeout, string actor);
}
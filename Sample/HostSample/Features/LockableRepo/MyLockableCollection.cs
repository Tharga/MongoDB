using MongoDB.Bson;
using Tharga.MongoDB;
using Tharga.MongoDB.Lockable;

namespace HostSample.Features.LockableRepo;

public class MyLockableCollection : LockableRepositoryCollectionBase<MyLockableEntity, ObjectId>, IMyLockableCollection
{
    public MyLockableCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<MyLockableCollection> logger)
        : base(mongoDbServiceFactory, logger)
    {
    }
}
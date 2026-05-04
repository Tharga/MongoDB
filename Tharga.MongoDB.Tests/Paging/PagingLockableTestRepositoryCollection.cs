using MongoDB.Bson;
using Tharga.MongoDB.Lockable;

namespace Tharga.MongoDB.Tests.Paging;

internal class PagingLockableTestRepositoryCollection : LockableRepositoryCollectionBase<PagingLockableTestEntity, ObjectId>
{
    public PagingLockableTestRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory)
        : base(mongoDbServiceFactory)
    {
    }

    protected override bool RequireActor => false;
}

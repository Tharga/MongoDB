using MongoDB.Bson;
using Tharga.MongoDB.Lockable;

namespace Tharga.MongoDB.Tests.Support;

internal class LockableTestRepositoryCollection : LockableRepositoryCollectionBase<LockableTestEntity, ObjectId>
{
    public LockableTestRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory)
        : base(mongoDbServiceFactory)
    {
    }

    protected override bool RequireActor => false;
}
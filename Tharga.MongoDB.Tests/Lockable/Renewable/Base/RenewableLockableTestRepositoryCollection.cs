using MongoDB.Bson;
using Tharga.MongoDB.Lockable.Renewable;
using Tharga.MongoDB.Tests.Support;

namespace Tharga.MongoDB.Tests.Lockable.Renewable.Base;

internal class RenewableLockableTestRepositoryCollection : RenewableLockRepositoryCollectionBase<LockableTestEntity, ObjectId>
{
    public RenewableLockableTestRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory)
        : base(mongoDbServiceFactory)
    {
    }

    protected override bool RequireActor => false;
}

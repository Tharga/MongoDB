using MongoDB.Bson;
using Tharga.MongoDB.Lockable.Renewable;
using Tharga.MongoDB.Tests.Support;

namespace Tharga.MongoDB.Tests.Lockable.Renewable.Base;

/// <summary>
/// Test-only renewable collection pinned to strict-TTL behaviour by overriding
/// AllowDelayedCommit, mirroring the StrictTtlTestRepositoryCollection used by the
/// non-renewable DelayedCommitTests. Used to verify that an expired renewal throws
/// LockExpiredException on a strict collection.
/// </summary>
internal sealed class StrictRenewableLockableTestRepositoryCollection : RenewableLockRepositoryCollectionBase<LockableTestEntity, ObjectId>
{
    public StrictRenewableLockableTestRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory)
        : base(mongoDbServiceFactory)
    {
    }

    protected override bool RequireActor => false;
    protected override bool AllowDelayedCommit => false;
}

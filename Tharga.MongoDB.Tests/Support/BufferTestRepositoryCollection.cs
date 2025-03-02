using MongoDB.Bson;
using Tharga.MongoDB.Buffer;
using Tharga.MongoDB.Lockable;

namespace Tharga.MongoDB.Tests.Support;

internal class LockableTestRepositoryCollection : LockableRepositoryCollectionBase<LockableTestEntity, ObjectId>
{
    public LockableTestRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory)
        : base(mongoDbServiceFactory)
    {
    }
}

internal class BufferTestRepositoryCollection : BufferRepositoryCollectionBase<TestEntity, ObjectId>
{
    public BufferTestRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, null, databaseContext)
    {
    }

    public override string CollectionName => "Test";
}
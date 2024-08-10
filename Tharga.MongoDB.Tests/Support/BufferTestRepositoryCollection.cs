using MongoDB.Bson;
using Tharga.MongoDB.Buffer;
using Tharga.MongoDB.Lockable;

namespace Tharga.MongoDB.Tests.Support;

public class LockableTestRepositoryCollection : LockableRepositoryCollectionBase<LockableTestEntity, ObjectId>
{
    protected LockableTestRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory)
        : base(mongoDbServiceFactory)
    {
    }
}

public class BufferTestRepositoryCollection : BufferRepositoryCollectionBase<TestEntity, ObjectId>
{
    public BufferTestRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, null, databaseContext)
    {
    }

    public override string CollectionName => "Test";
}
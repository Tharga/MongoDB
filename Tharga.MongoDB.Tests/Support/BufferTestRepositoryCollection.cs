using MongoDB.Bson;
using Tharga.MongoDB.Buffer;

namespace Tharga.MongoDB.Tests.Support;

public class BufferTestRepositoryCollection : BufferRepositoryCollectionBase<TestEntity, ObjectId>
{
    public BufferTestRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, null, databaseContext)
    {
    }

    public override string CollectionName => "Test";
}
using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Experimental;
using Tharga.MongoDB.Tests.Support;

namespace Tharga.MongoDB.Tests.Experimental;

public class BufferTestRepositoryCollection : ReadWriteBufferRepositoryCollectionBase<TestEntity, ObjectId>
{
    public BufferTestRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, null, databaseContext)
    {
    }

    public override string CollectionName => "Test";

    public override IEnumerable<CreateIndexModel<TestEntity>> Indicies => new[]
    {
        new CreateIndexModel<TestEntity>(Builders<TestEntity>.IndexKeys.Ascending(f => f.Value), new CreateIndexOptions { Unique = true, Name = nameof(TestEntity.Value) })
    };

    public override IEnumerable<Type> Types => new[] { typeof(TestSubEntity), typeof(TestEntity) };
}
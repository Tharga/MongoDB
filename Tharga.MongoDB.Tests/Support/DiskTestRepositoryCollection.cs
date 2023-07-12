using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB.Tests.Support;

public class DiskTestRepositoryCollection : DiskRepositoryCollectionBase<TestEntity, ObjectId>
{
    public DiskTestRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, null, databaseContext)
    {
    }

    public override string CollectionName => "Test";

    public override IEnumerable<CreateIndexModel<TestEntity>> Indicies => new[]
    {
        new CreateIndexModel<TestEntity>(Builders<TestEntity>.IndexKeys.Ascending(f => f.Value), new CreateIndexOptions { Unique = false, Name = nameof(TestEntity.Value) })
    };

    public override IEnumerable<Type> Types => new[] { typeof(TestSubEntity), typeof(TestEntity) };
}
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
    public override int? ResultLimit => 5;

    public override IEnumerable<CreateIndexModel<TestEntity>> Indices =>
    [
        new(Builders<TestEntity>.IndexKeys.Ascending(f => f.Value), new CreateIndexOptions { Unique = true, Name = nameof(TestEntity.Value) })
    ];

    public override IEnumerable<Type> Types => [typeof(TestSubEntity), typeof(TestEntity)];
}
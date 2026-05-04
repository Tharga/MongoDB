using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB.Tests.Paging;

internal class PagingTestRepositoryCollection : DiskRepositoryCollectionBase<PagingTestEntity, ObjectId>
{
    public PagingTestRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, null, databaseContext)
    {
    }

    public override string CollectionName => "PagingTest";

    public override IEnumerable<CreateIndexModel<PagingTestEntity>> Indices =>
    [
        new(Builders<PagingTestEntity>.IndexKeys.Ascending(f => f.Name).Ascending(f => f.Id), new CreateIndexOptions { Name = "name_id" }),
        new(Builders<PagingTestEntity>.IndexKeys.Ascending(f => f.Bucket).Ascending(f => f.Id), new CreateIndexOptions { Name = "bucket_id" }),
    ];

    public override IEnumerable<Type> Types => [typeof(PagingTestEntity)];
}

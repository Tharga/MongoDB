using System.Collections.Generic;
using HostSample.Entities;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace HostSample.Features.DynamicDiskRepo;

public class MyDynamicDiskRepoCollection : DiskRepositoryCollectionBase<MyEntity, ObjectId>, IMyDynamicDiskRepoCollection
{
    public MyDynamicDiskRepoCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext, ILogger<MyDynamicDiskRepoCollection> logger)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }

    public override IEnumerable<CreateIndexModel<MyEntity>> Indices => new[]
    {
        new CreateIndexModel<MyEntity>(Builders<MyEntity>.IndexKeys.Ascending(x => x.Counter), new CreateIndexOptions { Unique = false, Name = nameof(MyEntity.Counter) })
    };
}
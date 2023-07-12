using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.HostSample.Entities;

namespace Tharga.MongoDB.HostSample.Features.DynamicDiskRepo;

public class MyDynamicDiskRepoCollection : DiskRepositoryCollectionBase<MyEntity, ObjectId>, IMyDynamicDiskRepoCollection
{
    public MyDynamicDiskRepoCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext, ILogger<MyDynamicDiskRepoCollection> logger)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }

    public override IEnumerable<CreateIndexModel<MyEntity>> Indicies => new[]
    {
        new CreateIndexModel<MyEntity>(Builders<MyEntity>.IndexKeys.Ascending(x => x.Counter), new CreateIndexOptions { Unique = false, Name = nameof(MyEntity.Counter) })
    };
}
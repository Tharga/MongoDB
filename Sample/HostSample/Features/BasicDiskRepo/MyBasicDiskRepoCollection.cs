using HostSample.Entities;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace HostSample.Features.BasicDiskRepo;

public class MyBasicDiskRepoCollection : DiskRepositoryCollectionBase<MyEntity, ObjectId>, IMyBasicDiskRepoCollection
{
    public MyBasicDiskRepoCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<MyBasicDiskRepoCollection> logger)
        : base(mongoDbServiceFactory, logger)
    {
    }

    public override string DatabasePart => "MyDatabasePart";
    public override string CollectionName => "MyCollection";
}
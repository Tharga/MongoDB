using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.HostSample.Entities;

namespace Tharga.MongoDB.HostSample.Features.BasicDiskRepo;

public class MyBasicDiskRepoCollection : DiskRepositoryCollectionBase<MyEntity, ObjectId>, IMyBasicDiskRepoCollection
{
    public MyBasicDiskRepoCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<MyBasicDiskRepoCollection> logger)
        : base(mongoDbServiceFactory, logger)
    {
    }

    //public override string ConfigurationName => "Other";
    public override string DatabasePart => "MyDatabasePart";
    public override string CollectionName => "MyCollection";
}
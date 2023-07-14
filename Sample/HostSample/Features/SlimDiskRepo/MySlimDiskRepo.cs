using System.Collections.Generic;
using System.Threading.Tasks;
using HostSample.Entities;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace HostSample.Features.SlimDiskRepo;

public class MySlimDiskRepo : DiskRepositoryCollectionBase<MyEntity, ObjectId>
{
    public MySlimDiskRepo(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<MySlimDiskRepo> logger)
        : base(mongoDbServiceFactory, logger)
    {
    }

    //public override string ConfigurationName => "Other";
    public override string DatabasePart => "MyDatabasePart";
    public override string CollectionName => "MyCollection";

    public async Task<ObjectId> CreateRandom()
    {
        var myEntity = new MyEntity();
        await base.AddAsync(myEntity);
        return myEntity.Id;
    }

    public IAsyncEnumerable<MyEntity> GetAll()
    {
        return base.GetAsync(x => true);
    }
}
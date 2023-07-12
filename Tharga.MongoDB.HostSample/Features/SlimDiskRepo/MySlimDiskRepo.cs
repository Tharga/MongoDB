using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.HostSample.Entities;

namespace Tharga.MongoDB.HostSample.Features.SlimDiskRepo;

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
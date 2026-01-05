using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace ConsoleSample.SampleRepo;

internal class MyRepo : DiskRepositoryCollectionBase<MyBaseEntity, ObjectId>, IMyRepo
{
    public MyRepo(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<MyRepo> logger, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }
}
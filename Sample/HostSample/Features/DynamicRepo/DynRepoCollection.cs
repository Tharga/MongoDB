using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace HostSample.Features.DynamicRepo;

internal class DynRepoCollection : DiskRepositoryCollectionBase<DynRepoItem>, IDynRepoCollection
{
    public DynRepoCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<DynRepoCollection> logger, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }
}
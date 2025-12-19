using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace HostSample.Features.DynamicRepo;

internal class DynRepoCollection : DiskRepositoryCollectionBase<DynRepoItem>, IDynRepoCollection
{
    public DynRepoCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<DynRepoCollection> logger, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }

    public override IEnumerable<CreateIndexModel<DynRepoItem>> Indices =>
    [
        new(Builders<DynRepoItem>.IndexKeys.Ascending(f => f.Something), new CreateIndexOptions { Unique = false }),
        new(Builders<DynRepoItem>.IndexKeys.Ascending(f => f.Else), new CreateIndexOptions { Unique = false }),
    ];
}
using HostSample.Entities;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Tharga.MongoDB;

namespace HostSample.Features.Experimental;

public class ExperimentalDiskRepoCollection : Tharga.MongoDB.Experimental.ReadWriteDiskRepositoryCollectionBase<MyEntity, ObjectId>, IExperimentalDiskRepoCollection
{
    public ExperimentalDiskRepoCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<ExperimentalDiskRepoCollection> logger)
        : base(mongoDbServiceFactory, logger)
    {
    }

    public override string DatabasePart => "MyDatabasePart";
    public override string CollectionName => "MyCollection";
}
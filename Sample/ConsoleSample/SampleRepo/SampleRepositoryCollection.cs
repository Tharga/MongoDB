using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace ConsoleSample.SampleRepo;

internal class SampleRepositoryCollection : DiskRepositoryCollectionBase<SampleEntity>, ISampleRepositoryCollection
{
    public SampleRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<SampleRepositoryCollection> logger)
        : base(mongoDbServiceFactory, logger)
    {
    }

    public override IEnumerable<CreateIndexModel<SampleEntity>> Indices =>
    [
        new(Builders<SampleEntity>.IndexKeys.Ascending(x => x.Name),
            new CreateIndexOptions { Name = nameof(SampleEntity.Name), Unique = true })
    ];
}
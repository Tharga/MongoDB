using Microsoft.Extensions.Logging;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace ConsoleSample.SampleRepo;

internal class SampleRepositoryCollection : DiskRepositoryCollectionBase<SampleEntity>, ISampleRepositoryCollection
{
    public SampleRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<SampleRepositoryCollection> logger)
        : base(mongoDbServiceFactory, logger)
    {
    }
}
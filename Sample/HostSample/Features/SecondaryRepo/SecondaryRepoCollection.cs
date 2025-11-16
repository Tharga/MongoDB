using HostSample.Features.DiskRepo;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace HostSample.Features.SecondaryRepo;

internal class SecondaryRepoCollection : DiskRepositoryCollectionBase<WeatherForecast>, ISecondaryRepoCollection
{
    public SecondaryRepoCollection(IMongoDbServiceFactory mongoDbServiceFactory)
        : base(mongoDbServiceFactory)
    {
    }

    public override string ConfigurationName => "Secondary";
}
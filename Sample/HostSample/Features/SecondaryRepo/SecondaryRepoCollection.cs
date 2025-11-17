using HostSample.Features.DiskRepo;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace HostSample.Features.SecondaryRepo;

internal class SecondaryRepoCollection : DiskRepositoryCollectionBase<WeatherForecast>, ISecondaryRepoCollection
{
    public SecondaryRepoCollection(IMongoDbServiceFactory mongoDbServiceFactory)
        : base(mongoDbServiceFactory)
    {
    }

    public override IEnumerable<CreateIndexModel<WeatherForecast>> Indices =>
    [
        new(Builders<WeatherForecast>.IndexKeys.Ascending(f => f.Summary), new CreateIndexOptions { Unique = false, Name = nameof(WeatherForecast.Summary) }),
        new(Builders<WeatherForecast>.IndexKeys.Ascending(f => f.TemperatureC), new CreateIndexOptions { Unique = true, Name = nameof(WeatherForecast.TemperatureC) })
    ];

    public override string ConfigurationName => "Secondary";
}
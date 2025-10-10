using MongoDB.Bson;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace HostSample.Features.DiskRepo;

internal class WeatherForecastRepositoryCollection : DiskRepositoryCollectionBase<WeatherForecast>, IWeatherForecastRepositoryCollection
{
    public WeatherForecastRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<WeatherForecast, ObjectId>> logger)
        : base(mongoDbServiceFactory, logger)
    {
    }

    //public override string CollectionName => "SomeCustomName";
}
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace HostSample.Features.DiskRepo;

internal class WeatherForecastRepositoryCollection : DiskRepositoryCollectionBase<WeatherForecast>, IWeatherForecastRepositoryCollection
{
    public WeatherForecastRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<WeatherForecastRepositoryCollection> logger)
        : base(mongoDbServiceFactory, logger)
    {
    }
}
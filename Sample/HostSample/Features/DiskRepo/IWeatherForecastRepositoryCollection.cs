using Tharga.MongoDB;

namespace HostSample.Features.DiskRepo;

public interface IWeatherForecastRepositoryCollection : IDiskRepositoryCollection<WeatherForecast>
{
}
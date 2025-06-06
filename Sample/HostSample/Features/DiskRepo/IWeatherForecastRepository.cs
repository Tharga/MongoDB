using Tharga.MongoDB;

namespace HostSample.Features.DiskRepo;

public interface IWeatherForecastRepository : IRepository
{
    IAsyncEnumerable<WeatherForecast> GetAsync();
    Task AddRangeAsync(WeatherForecast[] weatherForecasts);
    IAsyncEnumerable<WeatherForecast> GetDirtyAsync();
    IEnumerable<(IndexFailOperation Operation, string Name)> GetFailedIndices();
}
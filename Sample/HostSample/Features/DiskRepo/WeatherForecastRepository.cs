using Tharga.MongoDB;

namespace HostSample.Features.DiskRepo;

internal class WeatherForecastRepository : IWeatherForecastRepository
{
    private readonly IWeatherForecastRepositoryCollection _collection;

    public WeatherForecastRepository(IWeatherForecastRepositoryCollection collection)
    {
        _collection = collection;
    }

    public IAsyncEnumerable<WeatherForecast> GetAsync()
    {
        return _collection.GetAsync();
    }

    public async Task AddRangeAsync(WeatherForecast[] weatherForecasts)
    {
        foreach (var weatherForecast in weatherForecasts)
        {
            await _collection.AddAsync(weatherForecast);
        }
    }

    public IAsyncEnumerable<WeatherForecast> GetDirtyAsync()
    {
        return _collection.GetDirtyAsync();
    }

    public IEnumerable<(IndexFailOperation Operation, string Name)> GetFailedIndices()
    {
        return _collection.GetFailedIndices();
    }
}
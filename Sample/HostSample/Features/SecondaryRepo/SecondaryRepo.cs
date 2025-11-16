using HostSample.Features.DiskRepo;

namespace HostSample.Features.SecondaryRepo;

internal class SecondaryRepo : ISecondaryRepo
{
    private readonly ISecondaryRepoCollection _collection;

    public SecondaryRepo(ISecondaryRepoCollection collection)
    {
        _collection = collection;
    }

    public IAsyncEnumerable<WeatherForecast> GetAsync()
    {
        return _collection.GetAsync();
    }

    public Task SetAsync(WeatherForecast weatherForecast)
    {
        return _collection.AddAsync(weatherForecast);
    }
}
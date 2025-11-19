using HostSample.Features.DiskRepo;
using Tharga.MongoDB;

namespace HostSample.Features.SecondaryRepo;

public interface ISecondaryRepo : IRepository
{
    IAsyncEnumerable<WeatherForecast> GetAsync();
    Task SetAsync(WeatherForecast weatherForecast);
}
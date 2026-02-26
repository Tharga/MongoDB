using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.TemplateBlazor.Web.Features.Weather;

public interface IWeatherRepository : IRepository
{
    Task AddMany(WeatherEntity[] forecasts);
    IAsyncEnumerable<WeatherEntity> GetAsync();
}

public class WeatherRepository : IWeatherRepository
{
    private readonly IWeatherRepositoryCollection _weatherRepositoryCollection;

    public WeatherRepository(IWeatherRepositoryCollection weatherRepositoryCollection)
    {
        _weatherRepositoryCollection = weatherRepositoryCollection;
    }

    public IAsyncEnumerable<WeatherEntity> GetAsync()
    {
        return _weatherRepositoryCollection.GetAsync();
    }

    public Task AddMany(WeatherEntity[] forecasts)
    {
        return _weatherRepositoryCollection.AddManyAsync(forecasts);
    }
}

public record WeatherEntity : EntityBase
{
    public required DateOnly Date { get; init; }
    public required int TemperatureC { get; init; }
    public required string? Summary { get; init; }
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public interface IWeatherRepositoryCollection : IDiskRepositoryCollection<WeatherEntity>
{
}

public class WeatherRepositoryCollection : DiskRepositoryCollectionBase<WeatherEntity>, IWeatherRepositoryCollection
{
    public WeatherRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<WeatherRepositoryCollection> logger)
        : base(mongoDbServiceFactory, logger)
    {
    }
}
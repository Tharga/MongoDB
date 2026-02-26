using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.TemplateBlazor.Web.Features.Weather;

public interface ILocalWeatherRepository : IRepository
{
    Task AddMany(string city, LocalWeatherEntity[] forecasts);
    IAsyncEnumerable<LocalWeatherEntity> GetAsync(string city);
}

public class LocalWeatherRepository : ILocalWeatherRepository
{
    private readonly ICollectionProvider _collectionProvider;

    public LocalWeatherRepository(ICollectionProvider collectionProvider)
    {
        _collectionProvider = collectionProvider;
    }

    public IAsyncEnumerable<LocalWeatherEntity> GetAsync(string city)
    {
        var collection = GetCollection(city);
        return collection.GetAsync();
    }

    public Task AddMany(string city, LocalWeatherEntity[] forecasts)
    {
        var collection = GetCollection(city);
        return collection.AddManyAsync(forecasts);
    }

    private ILocalWeatherRepositoryCollection GetCollection(string city)
    {
        return _collectionProvider.GetCollection<ILocalWeatherRepositoryCollection, LocalWeatherEntity>(new DatabaseContext { CollectionName = $"{city}_Weather" });
    }
}

public record LocalWeatherEntity : EntityBase
{
    public required DateOnly Date { get; init; }
    public required int TemperatureC { get; init; }
    public required string? Summary { get; init; }
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public interface ILocalWeatherRepositoryCollection : IDiskRepositoryCollection<LocalWeatherEntity>
{
}

public class LocalWeatherRepositoryCollection : DiskRepositoryCollectionBase<LocalWeatherEntity>, ILocalWeatherRepositoryCollection
{
    public LocalWeatherRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<LocalWeatherRepositoryCollection> logger, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }
}
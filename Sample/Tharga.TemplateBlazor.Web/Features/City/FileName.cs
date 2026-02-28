using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.TemplateBlazor.Web.Features.City;

public interface ICityRepository : IRepository
{
    Task Add(CityEntity item);
    IAsyncEnumerable<CityEntity> GetAsync();
    Task DeleteAsync(string city);
}

public class CityRepository : ICityRepository
{
    private readonly ICityRepositoryCollection _cityRepositoryCollection;

    public CityRepository(ICityRepositoryCollection cityRepositoryCollection)
    {
        _cityRepositoryCollection = cityRepositoryCollection;
    }

    public IAsyncEnumerable<CityEntity> GetAsync()
    {
        return _cityRepositoryCollection.GetAsync();
    }

    public Task DeleteAsync(string city)
    {
        return _cityRepositoryCollection.DeleteOneAsync(x => x.Name == city);
    }

    public Task Add(CityEntity item)
    {
        return _cityRepositoryCollection.AddAsync(item);
    }
}

public record CityEntity : EntityBase
{
    public required string Name { get; init; }
    public string Country { get; init; }
}

public interface ICityRepositoryCollection : IDiskRepositoryCollection<CityEntity>
{
}

public class CityRepositoryCollection : DiskRepositoryCollectionBase<CityEntity>, ICityRepositoryCollection
{
    public CityRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<CityRepositoryCollection> logger)
        : base(mongoDbServiceFactory, logger)
    {
    }

    public override IEnumerable<CreateIndexModel<CityEntity>> Indices =>
    [
        new(Builders<CityEntity>.IndexKeys.Ascending(f => f.Name), new CreateIndexOptions { Unique = true }),
        new(Builders<CityEntity>.IndexKeys.Ascending(f => f.Country), new CreateIndexOptions { Unique = false }),
        new(Builders<CityEntity>.IndexKeys.Descending(f => f.Country), new CreateIndexOptions { Unique = false }),
        new(Builders<CityEntity>.IndexKeys.Ascending(f => f.Country).Ascending(f => f.Name), new CreateIndexOptions { Unique = false }),
        new(Builders<CityEntity>.IndexKeys.Hashed(f => f.Country), new CreateIndexOptions { Unique = false, Name = "Yee" })
    ];
}
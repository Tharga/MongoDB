using HostSample.Features.DiskRepo;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace HostSample.Features.SecondaryRepo;

public interface ISecondaryRepo : IRepository
{
    IAsyncEnumerable<WeatherForecast> GetAsync();
    Task SetAsync(WeatherForecast weatherForecast);
}

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

public interface ISecondaryRepoCollection : IDiskRepositoryCollection<WeatherForecast>
{

}

internal class SecondaryRepoCollection : DiskRepositoryCollectionBase<WeatherForecast>, ISecondaryRepoCollection
{
    public SecondaryRepoCollection(IMongoDbServiceFactory mongoDbServiceFactory)
        : base(mongoDbServiceFactory)
    {
    }

    public override string ConfigurationName => "Secondary";
}
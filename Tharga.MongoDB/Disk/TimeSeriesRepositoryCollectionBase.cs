using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Tharga.MongoDB.Disk;

public abstract class TimeSeriesRepositoryCollectionBase<TEntity, TKey> : DiskRepositoryCollectionBase<TEntity, TKey>
    where TEntity : TimeSeriesEntityBase<TKey>
{
    protected TimeSeriesRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger = null, DatabaseContext databaseContext = null)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }

    protected abstract TimeSeriesGranularity Granularity { get; }

    protected override async Task<IMongoCollection<T>> GetCollectionAsync<T>()
    {
        var options = new TimeSeriesOptions(nameof(TimeSeriesEntityBase<TKey>.Timestamp), granularity: Granularity);
        return await _mongoDbService.GetCollectionAsync<T>(ProtectedCollectionName, options);
    }
}
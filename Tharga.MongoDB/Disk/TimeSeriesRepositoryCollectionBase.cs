using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Tharga.MongoDB.Disk;

public abstract class TimeSeriesRepositoryCollectionBase<TEntity, TMetadata, TKey> : DiskRepositoryCollectionBase<TEntity, TKey>
    where TEntity : TimeSeriesEntityBase<TMetadata, TKey>
{
    protected TimeSeriesRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger = null, DatabaseContext databaseContext = null)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }

    protected abstract TimeSeriesGranularity Granularity { get; }

    protected override async Task<IMongoCollection<T>> GetCollectionAsync<T>()
    {
        var options = new TimeSeriesOptions(nameof(TimeSeriesEntityBase<TKey, TMetadata>.Timestamp), granularity: Granularity, metaField: new Optional<string>("metadata") );
        return await _mongoDbService.GetCollectionAsync<T>(ProtectedCollectionName, options);
    }
}
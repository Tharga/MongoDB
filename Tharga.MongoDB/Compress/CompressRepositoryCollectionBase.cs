using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB.Compress;

public abstract class CompressRepositoryCollectionBase<TEntity, TKey> : RepositoryCollectionBase<TEntity, TKey>
    where TEntity : CompressEntityBase<TEntity, TKey>
{
    //TODO: Implement retention feature
    //TODO: Implement compression feature

    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private DiskRepositoryCollectionBase<TEntity, TKey> _disk;
    private bool _diskConnected = true;

    protected CompressRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<CompressRepositoryCollectionBase<TEntity, TKey>> logger = null, DatabaseContext databaseContext = null)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
    }

    internal override IRepositoryCollection<TEntity, TKey> BaseCollection => _diskConnected ? Disk : this;
    private RepositoryCollectionBase<TEntity, TKey> Disk => _diskConnected ? _disk ??= new GenericDiskRepositoryCollection<TEntity, TKey>(_mongoDbServiceFactory, _databaseContext ?? new DatabaseContext { CollectionName = CollectionName, DatabasePart = DatabasePart, ConfigurationName = ConfigurationName }, _logger, this) : null;

    public override IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        //TODO: Compress data here
        return Disk.GetAsync(predicate, options, cancellationToken);
    }

    public override IAsyncEnumerable<TEntity> GetAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        //TODO: Compress data here
        return Disk.GetAsync(filter, options, cancellationToken);
    }

    public override async IAsyncEnumerable<T> GetAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
        yield break;
    }

    public override Task<Result<TEntity, TKey>> QueryAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override async Task<Result<TEntity, TKey>> QueryAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override IAsyncEnumerable<ResultPage<TEntity, TKey>> GetPagesAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override async Task<TEntity> GetOneAsync(TKey id, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override Task<TEntity> GetOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetOneAsync(predicate, options, cancellationToken);
    }

    public override Task<TEntity> GetOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override async Task<T> GetOneAsync<T>(Expression<Func<T, bool>> predicate = null, OneOption<T> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override async Task AddAsync(TEntity entity)
    {
        //The last hour per minute
        //The last 24 hours per hour
        //The last 30 days per day
        //Per month

        var timeInfo = GetDateTime(entity);
        entity = entity with
        {
            Timestamp = timeInfo.Time,
            Granularity = timeInfo.Granularity
        };

        var item = await Disk.GetOneAsync(x => x.AggregateKey == entity.AggregateKey && x.Timestamp == timeInfo.Time);
        if (item == null)
        {
            await Disk.AddOrReplaceAsync(entity);
        }
        else
        {
            var merged = item.Merge(entity);
            await Disk.ReplaceOneAsync(merged);
        }
    }

    private static (DateTime? Time, CompressGranularity Granularity) GetDateTime(TEntity entity)
    {
        DateTime? time = null;
        var granularity = CompressGranularity.None;
        if (entity.Timestamp.HasValue)
        {
            var strata = entity.GetStrata();
            granularity = strata.CompressPer;
            switch (strata.CompressPer)
            {
                case CompressGranularity.None:
                    break;
                case CompressGranularity.Minute:
                    time = new DateTime(entity.Timestamp.Value.Year, entity.Timestamp.Value.Month, entity.Timestamp.Value.Day, entity.Timestamp.Value.Hour, entity.Timestamp.Value.Minute, 0);
                    break;
                case CompressGranularity.Hour:
                    time = new DateTime(entity.Timestamp.Value.Year, entity.Timestamp.Value.Month, entity.Timestamp.Value.Day, entity.Timestamp.Value.Hour, 0, 0);
                    break;
                case CompressGranularity.Day:
                    time = new DateTime(entity.Timestamp.Value.Year, entity.Timestamp.Value.Month, entity.Timestamp.Value.Day);
                    break;
                case CompressGranularity.Month:
                    time = new DateTime(entity.Timestamp.Value.Year, entity.Timestamp.Value.Month, 0);
                    break;
                case CompressGranularity.Year:
                    time = new DateTime(entity.Timestamp.Value.Year, 0, 0);
                    break;
                default:
                    throw new ArgumentOutOfRangeException($"Unknown strata value '{strata.CompressPer}'.");
            }
        }

        return (time ?? entity.Timestamp ?? DateTime.UtcNow, granularity);
    }

    public override async Task<bool> TryAddAsync(TEntity entity)
    {
        throw new NotImplementedException();
    }

    public override async Task AddManyAsync(IEnumerable<TEntity> entities)
    {
        throw new NotImplementedException();
    }

    public override async Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity)
    {
        throw new NotImplementedException();
    }

    public override async Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, OneOption<TEntity> options = null)
    {
        throw new NotImplementedException();
    }

    public override async Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update)
    {
        throw new NotImplementedException();
    }

    public override async Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, FindOneAndUpdateOptions<TEntity> options)
    {
        throw new NotImplementedException();
    }

    public override async Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, OneOption<TEntity> options = default)
    {
        throw new NotImplementedException();
    }

    public override async Task<TEntity> DeleteOneAsync(TKey id)
    {
        throw new NotImplementedException();
    }

    public override async Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate, FindOneAndDeleteOptions<TEntity, TEntity> options)
    {
        throw new NotImplementedException();
    }

    public override async Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate = default, OneOption<TEntity> options = default)
    {
        throw new NotImplementedException();
    }

    public override async Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public override async Task DropCollectionAsync()
    {
        throw new NotImplementedException();
    }

    public override async Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override Task<long> CountAsync(FilterDefinition<TEntity> filter, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override IAsyncEnumerable<TTarget> AggregateAsync<TTarget>(FilterDefinition<TEntity> filter, EPrecision precision, AggregateOperations<TTarget> operations, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override Task<long> GetSizeAsync()
    {
        throw new NotImplementedException();
    }

    internal Task DisconnectDiskAsync()
    {
        _diskConnected = false;
        return Task.CompletedTask;
    }

    internal Task ReconnectDiskAsync()
    {
        _diskConnected = true;
        return Task.CompletedTask;
    }
}
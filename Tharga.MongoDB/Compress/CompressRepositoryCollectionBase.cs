using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

    protected virtual IEnumerable<Strata> Stratas => null;
    public override IEnumerable<CreateIndexModel<TEntity>> Indicies => new CreateIndexModel<TEntity>[]
    {
        new(Builders<TEntity>.IndexKeys.Ascending(x => x.Timestamp), new CreateIndexOptions { Unique = false, Name = nameof(CompressEntityBase<TEntity, TKey>.Timestamp) }),
        new(Builders<TEntity>.IndexKeys.Ascending(x => x.Granularity), new CreateIndexOptions { Unique = false, Name = nameof(CompressEntityBase<TEntity, TKey>.Granularity) }),
    };

    public override async IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        await CompressAsync(cancellationToken);
        await foreach (var item in Disk.GetAsync(predicate, options, cancellationToken))
        {
            yield return item;
        }
    }

    public override async IAsyncEnumerable<TEntity> GetAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        await CompressAsync(cancellationToken);
        await foreach (var item in Disk.GetAsync(filter, options, cancellationToken))
        {
            yield return item;
        }
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

    private async Task CompressAsync(CancellationToken cancellationToken)
    {
        //TODO: Do not execute this on every call, just do it when something might have changed. Different stratas should have different intervals.
        //TODO: Also apply retention rules to the data.
        //TODO: Have a service do this in the background, even if data is not accessed.

        foreach (var strata in Stratas.OrderByDescending(x => x.WhenOlderThan))
        {
            var time = DateTime.UtcNow.Subtract(StrataHelper.GetTimeSpan(strata.CompressPer));
            var toMove = await Disk.GetAsync(x => x.Timestamp < time && x.Granularity < strata.CompressPer, cancellationToken: cancellationToken).ToArrayAsync(cancellationToken: cancellationToken);
            if (toMove.Any())
            {
                _logger.LogInformation("Compressing {collectionName} {count} items older than {olderThan} to {compressPer}.", CollectionName, toMove.Length, strata.WhenOlderThan, strata.CompressPer);

                foreach (var entity1 in toMove)
                {
                    var timeInfo = GetDateTime(entity1);
                    var entity = entity1 with
                    {
                        Timestamp = timeInfo.Time,
                        Granularity = timeInfo.Granularity
                    };

                    var item = await Disk.DeleteOneAsync(x => x.AggregateKey == entity.AggregateKey && x.Timestamp == timeInfo.Time);
                    try
                    {
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
                    catch (Exception e)
                    {
                        Debugger.Break();
                        _logger.LogError($"Item was deleted, but could not be merged. Trying to add it back again. {e.Message}", e);
                        try
                        {
                            await Disk.AddAsync(item);
                        }
                        catch (Exception exception)
                        {
                            Debugger.Break();
                            _logger.LogCritical($"Could not add item back to database. The data is lost. {exception.Message}", exception);
                            throw;
                        }
                    }
                }
            }
        }
    }

    private (DateTime? Time, CompressGranularity Granularity) GetDateTime(TEntity entity)
    {
        DateTime? time = null;
        var granularity = CompressGranularity.None;
        if (entity.Timestamp.HasValue)
        {
            var strata = StrataHelper.GetStrata(Stratas, entity.Timestamp.Value);
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
}
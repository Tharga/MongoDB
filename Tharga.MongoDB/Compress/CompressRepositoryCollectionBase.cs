using System;
using System.Collections.Concurrent;
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
    // ReSharper disable once StaticMemberInGenericType
    private static readonly ConcurrentDictionary<string, DateTime> _compressHistory = new();
    private readonly SemaphoreSlim _compressLock = new(1, 1);
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private DiskRepositoryCollectionBase<TEntity, TKey> _disk;
    private bool _diskConnected = true;

    protected CompressRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<CompressRepositoryCollectionBase<TEntity, TKey>> logger = null, DatabaseContext databaseContext = null)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        VerifySetup();
    }

    private void VerifySetup()
    {
        if (Debugger.IsAttached)
        {
            if (Stratas.GroupBy(x => x.WhenOlderThan).Any(x => x.Count() > 1)) throw new InvalidOperationException($"There are more than one value for the same '{nameof(Strata.WhenOlderThan)}' value for '{CollectionName}'.");
        }
    }

    internal override IRepositoryCollection<TEntity, TKey> BaseCollection => _diskConnected ? Disk : this;
    private RepositoryCollectionBase<TEntity, TKey> Disk => _diskConnected ? _disk ??= new GenericDiskRepositoryCollection<TEntity, TKey>(_mongoDbServiceFactory, _databaseContext ?? new DatabaseContext { CollectionName = CollectionName, DatabasePart = DatabasePart, ConfigurationName = ConfigurationName }, _logger, this) : null;

    protected virtual IEnumerable<Strata> Stratas => null;
    internal override IEnumerable<CreateIndexModel<TEntity>> CoreIndices => new CreateIndexModel<TEntity>[]
    {
        new(Builders<TEntity>.IndexKeys.Ascending(x => x.Timestamp), new CreateIndexOptions { Unique = false, Name = nameof(CompressEntityBase<TEntity, TKey>.Timestamp) }),
        new(Builders<TEntity>.IndexKeys.Ascending(x => x.Granularity), new CreateIndexOptions { Unique = false, Name = nameof(CompressEntityBase<TEntity, TKey>.Granularity) }),
        new(Builders<TEntity>.IndexKeys.Ascending(x => x.AggregateKey), new CreateIndexOptions { Unique = false, Name = nameof(CompressEntityBase<TEntity, TKey>.AggregateKey) }),
    };

    public override async IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await CompressAsync(cancellationToken);
        await foreach (var item in Disk.GetAsync(predicate, options, cancellationToken))
        {
            yield return item;
        }
    }

    public override async IAsyncEnumerable<TEntity> GetAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await CompressAsync(cancellationToken);
        await foreach (var item in Disk.GetAsync(filter, options, cancellationToken))
        {
            yield return item;
        }
    }

    public override async IAsyncEnumerable<T> GetAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await CompressAsync(cancellationToken);
        await foreach (var item in Disk.GetAsync(predicate, options, cancellationToken))
        {
            yield return item;
        }
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

    public override Task<TEntity> GetOneAsync(TKey id, CancellationToken cancellationToken = default)
    {
        return Disk.GetOneAsync(id, cancellationToken);
    }

    public override Task<TEntity> GetOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetOneAsync(predicate, options, cancellationToken);
    }

    public override Task<TEntity> GetOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetOneAsync(filter, options, cancellationToken);
    }

    public override Task<T> GetOneAsync<T>(Expression<Func<T, bool>> predicate = null, OneOption<T> options = null, CancellationToken cancellationToken = default)
    {
        return Disk.GetOneAsync(predicate, options, cancellationToken);
    }

    public override async Task AddAsync(TEntity entity)
    {
        if (!entity.Timestamp.HasValue) throw new InvalidOperationException($"Cannot save an entity of type '{typeof(TEntity).Name}' without a {nameof(entity.Timestamp)}.");
        if (entity.Timestamp?.Kind != DateTimeKind.Utc) throw new InvalidOperationException($"{nameof(entity.Timestamp)} for entity of type '{typeof(TEntity).Name}' must be Utc. {entity.Timestamp?.Kind} was provided.");

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

    public override Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, OneOption<TEntity> options = null)
    {
        return Disk.ReplaceOneAsync(entity, options);
    }

    public override Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update)
    {
        return Disk.UpdateOneAsync(id, update);
    }

    [Obsolete("Use UpdateOneAsync with 'OneOption' instead.")]
    public override Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, FindOneAndUpdateOptions<TEntity> options)
    {
        throw new NotSupportedException("This method has been deprecated.");
    }

    public override Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, OneOption<TEntity> options = default)
    {
        return Disk.UpdateOneAsync(filter, update, options);
    }

    public override Task<TEntity> DeleteOneAsync(TKey id)
    {
        return Disk.DeleteOneAsync(id);
    }

    [Obsolete("Use DeleteOneAsync with 'OneOption' instead.")]
    public override Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate, FindOneAndDeleteOptions<TEntity, TEntity> options)
    {
        throw new NotSupportedException("This method has been deprecated.");
    }

    public override Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate = default, OneOption<TEntity> options = default)
    {
        return Disk.DeleteOneAsync(predicate, options);
    }

    public override Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate)
    {
        return Disk.DeleteManyAsync(predicate);
    }

    public override IMongoCollection<TEntity> GetCollection()
    {
        throw new NotSupportedException();
    }

    public override Task DropCollectionAsync()
    {
        return Disk.DropCollectionAsync();
    }

    public override async Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        await CompressAsync(cancellationToken);
        return await Disk.CountAsync(predicate, cancellationToken);
    }

    public override async Task<long> CountAsync(FilterDefinition<TEntity> filter, CancellationToken cancellationToken = default)
    {
        await CompressAsync(cancellationToken);
        return await Disk.CountAsync(filter, cancellationToken);
    }

    public override IAsyncEnumerable<TTarget> AggregateAsync<TTarget>(FilterDefinition<TEntity> filter, EPrecision precision, AggregateOperations<TTarget> operations, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override Task<long> GetSizeAsync()
    {
        return Disk.GetSizeAsync();
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

    //TODO: Have a service do this in the background, even if data is not accessed.
    private async Task CompressAsync(CancellationToken cancellationToken)
    {
        if (!ShouldCompress()) return;

        await _compressLock.WaitAsync(cancellationToken);
        try
        {
            foreach (var strata in Stratas.OrderByDescending(x => x.WhenOlderThan))
            {
                var time = DateTime.UtcNow.Subtract(StrataHelper.GetTimeSpan(strata.WhenOlderThan));
                var toCompress = await Disk.GetAsync(x => x.Timestamp < time && x.Granularity < strata.CompressPer, cancellationToken: cancellationToken).ToArrayAsync(cancellationToken: cancellationToken);
                if (toCompress.Any())
                {
                    if (strata.CompressPer == CompressGranularity.Drop)
                    {
                        _logger.LogInformation("Dropping {collectionName} {count} items older than {olderThan}.", CollectionName, toCompress.Length, strata.WhenOlderThan);
                        InvokeAction(new ActionEventArgs.ActionData { Operation = "Compress-Drop", Message = $"Dropping {CollectionName} {toCompress.Length} items older than {strata.WhenOlderThan}.", Level = LogLevel.Information });

                        foreach (var entityToDelete in toCompress)
                        {
                            await Disk.DeleteOneAsync(entityToDelete.Id);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Compressing {collectionName} {count} items older than {olderThan} to {compressPer}.", CollectionName, toCompress.Length, strata.WhenOlderThan, strata.CompressPer);
                        InvokeAction(new ActionEventArgs.ActionData { Operation = "Compress", Message = $"Compressing {CollectionName} {toCompress.Length} items older than {strata.WhenOlderThan} to {strata.CompressPer}.", Level = LogLevel.Information });

                        foreach (var enitytToMerge in toCompress)
                        {
                            var timeInfo = GetDateTime(enitytToMerge);

                            var aggregate = await Disk.GetOneAsync(x => x.AggregateKey == enitytToMerge.AggregateKey && x.Timestamp == timeInfo.Time && x.Granularity == timeInfo.Granularity, cancellationToken: cancellationToken);
                            var item = await Disk.GetOneAsync(enitytToMerge.Id, cancellationToken);
                            if (aggregate == null)
                            {
                                //NOTE: The first time the entity changes granularity.
                                var merged = item with
                                {
                                    Timestamp = timeInfo.Time,
                                    Granularity = timeInfo.Granularity
                                };
                                await Disk.ReplaceOneAsync(merged);
                            }
                            else
                            {
                                //NOTE: Merge with another entity with the same granularity.
                                var merged = aggregate.Merge(item);
                                await Disk.DeleteOneAsync(enitytToMerge.Id);
                                try
                                {
                                    await Disk.ReplaceOneAsync(merged);
                                }
                                catch (Exception e)
                                {
                                    Debugger.Break();
                                    var message = $"Item was deleted, but could not be merged. Trying to add it back again. {e.Message}";
                                    _logger.LogError(message, e);
                                    InvokeAction(new ActionEventArgs.ActionData { Operation = "Compress", Message = message, Level = LogLevel.Error });
                                    try
                                    {
                                        await Disk.AddAsync(enitytToMerge);
                                    }
                                    catch (Exception exception)
                                    {
                                        Debugger.Break();
                                        var msg = $"Could not add item back to database. The data is lost. {exception.Message}";
                                        _logger.LogCritical(msg, exception);
                                        InvokeAction(new ActionEventArgs.ActionData { Operation = "Compress", Message = msg, Level = LogLevel.Critical });
                                        throw;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            _compressHistory.TryAdd(CollectionName, DateTime.UtcNow);
            _compressLock.Release();
        }
    }

    private bool ShouldCompress()
    {
        if (_compressHistory.TryGetValue(CollectionName, out var last))
        {
            var since = DateTime.UtcNow - last;
            var compressGranularity = Stratas.Where(x => x.WhenOlderThan != CompressGranularity.None).MinBy(x => x.WhenOlderThan)?.WhenOlderThan;
            switch (compressGranularity)
            {
                case null:
                case CompressGranularity.None:
                    return false;
                case CompressGranularity.Minute:
                case CompressGranularity.Hour:
                    if (since.TotalMinutes < 1) return false;
                    break;
                case CompressGranularity.Day:
                case CompressGranularity.Week:
                case CompressGranularity.Month:
                    if (since.TotalHours < 1) return false;
                    break;
                case CompressGranularity.Quarter:
                case CompressGranularity.Year:
                    if (since.TotalDays < 1) return false;
                    break;
                case CompressGranularity.Drop:
                    throw new NotSupportedException($"Strata granularity '{compressGranularity}' is not allowd.");
                default:
                    throw new ArgumentOutOfRangeException($"Unknown strata granularity '{compressGranularity}'.");
            }
        }

        return true;
    }

    private (DateTime? Time, CompressGranularity Granularity) GetDateTime(TEntity entity)
    {
        DateTime? time = null;
        var granularity = CompressGranularity.None;
        if (entity.Timestamp.HasValue)
        {
            var strata = StrataHelper.GetStrata(Stratas, entity.Timestamp.Value);
            granularity = strata?.CompressPer ?? CompressGranularity.None;
            switch (strata?.CompressPer)
            {
                case null:
                case CompressGranularity.None:
                    time = entity.Timestamp.Value;
                    break;
                case CompressGranularity.Minute:
                    time = new DateTime(entity.Timestamp.Value.Year, entity.Timestamp.Value.Month, entity.Timestamp.Value.Day, entity.Timestamp.Value.Hour, entity.Timestamp.Value.Minute, 0, DateTimeKind.Utc);
                    break;
                case CompressGranularity.Hour:
                    time = new DateTime(entity.Timestamp.Value.Year, entity.Timestamp.Value.Month, entity.Timestamp.Value.Day, entity.Timestamp.Value.Hour, 0, 0, DateTimeKind.Utc);
                    break;
                case CompressGranularity.Day:
                    time = new DateTime(entity.Timestamp.Value.Year, entity.Timestamp.Value.Month, entity.Timestamp.Value.Day, 0, 0, 0, DateTimeKind.Utc);
                    break;
                case CompressGranularity.Month:
                    time = new DateTime(entity.Timestamp.Value.Year, entity.Timestamp.Value.Month, 0, 0, 0, 0, DateTimeKind.Utc);
                    break;
                case CompressGranularity.Year:
                    time = new DateTime(entity.Timestamp.Value.Year, 0, 0, 0, 0, 0, DateTimeKind.Utc);
                    break;
                default:
                    throw new ArgumentOutOfRangeException($"Unknown strata value '{strata.CompressPer}'.");
            }
        }

        return (time ?? entity.Timestamp ?? DateTime.UtcNow, granularity);
    }
}
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

namespace Tharga.MongoDB.Buffer;

/// <summary>
/// This repo loads the entire collection into memory when used the first time and performs actions from there.
/// It should only be used when there is only a single consumer for the database, since changes to the database is not directly reflected to the cached data set.
/// </summary>
/// <typeparam name="TEntity"></typeparam>
/// <typeparam name="TKey"></typeparam>
public abstract class BufferRepositoryCollectionBase<TEntity, TKey> : RepositoryCollectionBase<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private readonly IBufferCollection<TEntity, TKey> _bufferCollection;
    private readonly SemaphoreSlim _bufferLoadLock = new(1, 1);
    private DiskRepositoryCollectionBase<TEntity, TKey> _disk;
    private bool _diskConnected = true;

    protected BufferRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<BufferRepositoryCollectionBase<TEntity, TKey>> logger = null, DatabaseContext databaseContext = null)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _bufferCollection = BufferLibrary.GetBufferCollection<TEntity, TKey>(databaseContext);
    }

    internal override IRepositoryCollection<TEntity, TKey> BaseCollection => _diskConnected ? Disk : this;
    private RepositoryCollectionBase<TEntity, TKey> Disk => _diskConnected ? _disk ??= new GenericDiskRepositoryCollection<TEntity, TKey>(_mongoDbServiceFactory, _databaseContext ?? new DatabaseContext { CollectionName = CollectionName, DatabasePart = DatabasePart, ConfigurationName = ConfigurationName }, _logger, this) : null;

    public override async IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (options != null) throw new NotSupportedException($"Parameter {nameof(options)} is not supported for {nameof(BufferRepositoryCollectionBase<TEntity, TKey>)}.");

        var sw = new Stopwatch();
        sw.Start();

        var buffer = await GetBufferAsync();
        var data = buffer.Values.Where(x => predicate?.Compile().Invoke(x) ?? true);
        var count = 0;
        foreach (var entity in data)
        {
            count++;
            yield return entity;
        }

        sw.Stop();
        _logger?.LogInformation($"Executed {{repositoryType}} for {{CollectionName}} took {{elapsed}} ms and returned {{itemCount}} items. [action: Database, operation: {nameof(GetAsync)}]", "BufferRepository", CollectionName, sw.Elapsed.TotalMilliseconds, count);
        InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(GetAsync), Elapsed = sw.Elapsed, ItemCount = count });
    }

    public override IAsyncEnumerable<TEntity> GetAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"{nameof(GetAsync)} with {nameof(filter)} is not supported for {nameof(BufferRepositoryCollectionBase<TEntity, TKey>)}");
    }

    public override async IAsyncEnumerable<T> GetAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (options != default) throw new NotSupportedException($"The {nameof(options)} parameter is not supported for {nameof(BufferRepositoryCollectionBase<TEntity, TKey>)}.");
        var buffer = await GetBufferAsync();
        var data = buffer.Values
            .Where(x => x.GetType() == typeof(T))
            .Where(x => (predicate ?? (_ => true)).Compile().Invoke(x as T));
        foreach (var entity in data)
        {
            yield return entity as T;
        }
    }

    public override IAsyncEnumerable<ResultPage<TEntity, TKey>> GetPagesAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"{nameof(GetPagesAsync)} is not supported for {nameof(BufferRepositoryCollectionBase<TEntity, TKey>)}");
    }

    public override async Task<TEntity> GetOneAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var buffer = await GetBufferAsync();
        var data = buffer.Values.FirstOrDefault(x => x.Id.Equals(id));
        return data;
    }

    public override async Task<TEntity> GetOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        if (options?.Sort != null) throw new NotSupportedException($"The {nameof(options.Sort)} part of the {nameof(options)} parameter is not supported for {nameof(BufferRepositoryCollectionBase<TEntity, TKey>)}.");

        var buffer = await GetBufferAsync();
        var data = buffer.Values.Where(x => predicate?.Compile().Invoke(x) ?? true);

        switch (options?.Mode)
        {
            case null:
            case EMode.Single:
                return data.SingleOrDefault();
            case EMode.First:
                return data.FirstOrDefault();
            default:
                throw new ArgumentOutOfRangeException($"Unknown mode '{options.Mode}'.");
        }
    }

    public override Task<TEntity> GetOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"{nameof(GetOneAsync)} with {nameof(filter)} is not supported for {nameof(BufferRepositoryCollectionBase<TEntity, TKey>)}");
    }

    public override async Task<T> GetOneAsync<T>(Expression<Func<T, bool>> predicate = null, OneOption<T> options = null, CancellationToken cancellationToken = default)
    {
        if (options?.Sort != null) throw new NotSupportedException($"The {nameof(options.Sort)} part of the {nameof(options)} parameter is not supported for {nameof(BufferRepositoryCollectionBase<TEntity, TKey>)}.");

        var buffer = await GetBufferAsync();
        var data = buffer.Values
            .Where(x => x.GetType() == typeof(T))
            .Where(x => (predicate ?? (_ => true)).Compile().Invoke(x as T));

        switch (options?.Mode)
        {
            case null:
            case EMode.Single:
                return data.SingleOrDefault() as T;
            case EMode.First:
                return data.FirstOrDefault() as T;
            default:
                throw new ArgumentOutOfRangeException($"Unknown mode '{options.Mode}'.");
        }
    }

    public override async Task AddAsync(TEntity entity)
    {
        await Disk.AddAsync(entity);

        var buffer = await GetBufferAsync();
        if (buffer.TryAdd(entity.Id, entity)) return;

        await InvalidateBufferAsync();
    }

    public override async Task<bool> TryAddAsync(TEntity entity)
    {
        var result = await Disk.TryAddAsync(entity);
        if (!result) return false;

        var buffer = await GetBufferAsync();
        if (buffer.TryAdd(entity.Id, entity)) return true;

        await InvalidateBufferAsync();
        return true;
    }

    public override async Task AddManyAsync(IEnumerable<TEntity> entities)
    {
        try
        {
            var array = entities as TEntity[] ?? entities.ToArray();

            await Disk.AddManyAsync(array);

            var buffer = await GetBufferAsync();
            foreach (var entity in array)
            {
                if (!buffer.TryAdd(entity.Id, entity))
                {
                    await InvalidateBufferAsync();
                    return;
                }
            }
        }
        catch
        {
            await InvalidateBufferAsync();
            throw;
        }
    }

    public override async Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity)
    {
        var result = await Disk.AddOrReplaceAsync(entity);
        _bufferCollection.Data.AddOrUpdate(entity.Id, entity, (_, _) => entity);
        return result;
    }

    public override async Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update)
    {
        var result = await Disk.UpdateOneAsync(id, update);
        await InvalidateBufferAsync();
        return result;
    }

    public override async Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, FindOneAndUpdateOptions<TEntity> options = default)
    {
        var result = await Disk.UpdateOneAsync(filter, update, options);
        await InvalidateBufferAsync();
        return result;
    }

    public override async Task<TEntity> DeleteOneAsync(TKey id)
    {
        var result = await Disk.DeleteOneAsync(id);
        if (!_bufferCollection.Data.TryRemove(id, out _))
        {
            await InvalidateBufferAsync();
        }
        return result;
    }

    public override async Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate = null, FindOneAndDeleteOptions<TEntity, TEntity> options = default)
    {
        var result = await Disk.DeleteOneAsync(predicate, options);
        if (!_bufferCollection.Data.TryRemove(result.Id, out _))
        {
            await InvalidateBufferAsync();
        }
        return result;
    }

    public override async Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate)
    {
        var result = await Disk.DeleteManyAsync(predicate);
        if (result > 0)
        {
            await InvalidateBufferAsync();
        }
        return result;
    }

    public override async Task DropCollectionAsync()
    {
        await Disk.DropCollectionAsync();
        await InvalidateBufferAsync();
    }

    public override async Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate)
    {
        var buffer = await GetBufferAsync();
        var data = buffer.Values.Where(x => predicate.Compile().Invoke(x));
        return data.Count();
    }

    public override Task<long> CountAsync(FilterDefinition<TEntity> filter)
    {
        throw new NotSupportedException($"{nameof(CountAsync)} with {nameof(filter)} is not supported for {nameof(BufferRepositoryCollectionBase<TEntity, TKey>)}");
    }

    public override Task<long> GetSizeAsync()
    {
        return Disk.GetSizeAsync();
    }

    /// <summary>
    /// Reloads the database content into memory.
    /// </summary>
    /// <returns></returns>
    public async Task InvalidateBufferAsync()
    {
        await GetBufferAsync(true);
    }

    private async ValueTask<ConcurrentDictionary<TKey, TEntity>> GetBufferAsync(bool forceReload = false)
    {
        if (!forceReload && _bufferCollection.Data != null) return _bufferCollection.Data;

        var sw = new Stopwatch();
        sw.Start();

        try
        {
            await _bufferLoadLock.WaitAsync();
            if (!forceReload && _bufferCollection.Data != null) return _bufferCollection.Data;

            var allData = await Disk.GetAsync(x => true).ToArrayAsync();
            _bufferCollection.Set(new ConcurrentDictionary<TKey, TEntity>(allData.ToDictionary(x => x.Id, x => x)));

            sw.Stop();
            _logger?.LogInformation($"Loaded {{repositoryType}} for collection {{collectionName}} took {{elapsed}} ms, contains {{itemCount}} items. Load was {{mode}}. [action: Database, operation: {nameof(GetBufferAsync)}]", "BufferRepository", ProtectedCollectionName, sw.Elapsed.TotalMilliseconds, allData.Length, forceReload ? "forced" : "initial");
            InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(GetBufferAsync), Elapsed = sw.Elapsed, ItemCount = allData.Length, Data = new Dictionary<string, object> { { "forceReload", forceReload ? "forced" : "initial" } }, });
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, $"Error when loading {{repositoryType}} for collection {{collectionName}}. [action: Database, operation: {nameof(GetBufferAsync)}]", "BufferRepository", ProtectedCollectionName);
            InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(GetBufferAsync), Exception = exception });
            throw;
        }
        finally
        {
            _bufferLoadLock.Release();
        }

        return _bufferCollection.Data;
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
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tharga.MongoDB.Buffer;

namespace Tharga.MongoDB.Experimental;

public abstract class ReadOnlyBufferRepositoryCollectionBase<TEntity, TKey> : RepositoryCollectionBase<TEntity, TKey>, IReadOnlyBufferRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    protected readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private readonly IBufferCollection<TEntity, TKey> _bufferCollection;
    private readonly SemaphoreSlim _bufferLoadLock = new(1, 1);
    private ReadOnlyDiskRepositoryCollectionBase<TEntity, TKey> _readOnlydisk;
    internal bool _diskConnected = true;

    protected ReadOnlyBufferRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<ReadOnlyBufferRepositoryCollectionBase<TEntity, TKey>> logger = null, DatabaseContext databaseContext = null)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _bufferCollection = BufferLibrary.GetBufferCollection<TEntity, TKey>();
    }

    internal virtual ReadOnlyDiskRepositoryCollectionBase<TEntity, TKey> Disk => _diskConnected ? _readOnlydisk ??= new GenericReadOnlyDiskRepositoryCollection<TEntity, TKey>(_mongoDbServiceFactory, _databaseContext ?? new DatabaseContext { CollectionName = CollectionName, DatabasePart = DatabasePart, ConfigurationName = ConfigurationName }, _logger, this) : null;

    public override Task<long> GetSizeAsync()
    {
        throw new NotImplementedException();
    }

    public override async IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
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
        _logger?.LogInformation($"Executed {{repositoryType}} took {{elapsed}} ms and returned {{itemCount}} items. [action: Database, operation: {nameof(GetAsync)}]", "BufferRepository", sw.Elapsed.TotalMilliseconds, count);
        InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(GetAsync), Elapsed = sw.Elapsed, ItemCount = count });
    }

    public override IAsyncEnumerable<T> GetAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override Task<TEntity> GetOneAsync(TKey id, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override Task<TEntity> GetOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override Task<T> GetOneAsync<T>(Expression<Func<T, bool>> predicate = null, OneOption<T> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate)
    {
        throw new NotImplementedException();
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

    internal override Task AssureIndex()
    {
        return Task.CompletedTask;
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
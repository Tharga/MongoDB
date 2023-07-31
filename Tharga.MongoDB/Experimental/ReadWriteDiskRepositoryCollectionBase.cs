using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB.Experimental;

public abstract class ReadWriteDiskRepositoryCollectionBase<TEntity, TKey> : ReadOnlyDiskRepositoryCollectionBase<TEntity, TKey>, IDiskRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    protected ReadWriteDiskRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger = null, DatabaseContext databaseContext = null)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }

    public virtual bool AutoClean => _mongoDbService.GetAutoClean();
    public virtual bool CleanOnStartup => _mongoDbService.GetCleanOnStartup();
    public virtual bool DropEmptyCollections => _mongoDbService.DropEmptyCollections();
    public virtual IEnumerable<CreateIndexModel<TEntity>> Indicies => null;

    public Task DropCollectionAsync()
    {
        throw new NotImplementedException();
    }

    public async Task<bool> AddAsync(TEntity entity)
    {
        return await Execute(nameof(AddAsync), async () =>
        {
            var existing = await Collection.CountDocumentsAsync(x => x.Id.Equals(entity.Id), null, CancellationToken.None);
            if (existing > 0) return false;
            await Collection.InsertOneAsync(entity);
            return true;
        }, true);
    }

    public Task AddManyAsync(IEnumerable<TEntity> entities)
    {
        throw new NotImplementedException();
    }

    public Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity)
    {
        throw new NotImplementedException();
    }

    public Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update)
    {
        throw new NotImplementedException();
    }

    public Task<TEntity> DeleteOneAsync(TKey id)
    {
        throw new NotImplementedException();
    }

    public Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate = null, FindOneAndDeleteOptions<TEntity, TEntity> options = default)
    {
        throw new NotImplementedException();
    }

    public Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public Task<long> UpdateAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update)
    {
        throw new NotImplementedException();
    }

    public Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, FindOneAndUpdateOptions<TEntity> options = default)
    {
        throw new NotImplementedException();
    }

    internal override Task AssureIndex()
    {
        return AssureIndex(Collection);
    }

    internal override async Task AssureIndex(IMongoCollection<TEntity> collection)
    {
        if (InitiationLibrary.ShouldInitiateIndex(ServerName, DatabaseName, ProtectedCollectionName))
        {
            await collection.Indexes.CreateOneAsync(new CreateIndexModel<TEntity>(Builders<TEntity>.IndexKeys.Ascending(x => x.Id).Ascending("_t"), new CreateIndexOptions()));
            await UpdateIndiciesAsync(collection);
        }
    }

    internal override async Task CleanAsync(IMongoCollection<TEntity> collection)
    {
        if (!CleanOnStartup) return;

        if (!AutoClean)
        {
            _logger?.LogWarning($"Both CleanOnStartup and AutoClean for collection {{collectionName}} has to be true for cleaning to run on startup. [action: Database, operation: {nameof(CleanAsync)}]", ProtectedCollectionName);
            InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(CleanAsync), Message = "Both CleanOnStartup and AutoClean for has to be true for cleaning to run on startup.", Level = LogLevel.Warning });
            return;
        }

        var sw = new Stopwatch();
        sw.Start();

        var filter = Builders<TEntity>.Filter.Empty;

        var cursor = await FindAsync(collection, filter, CancellationToken.None, null);
        var allItems = await cursor.ToListAsync();
        var items = allItems.Where(x => x.NeedsCleaning());
        var totalCount = allItems.Count;
        var count = 0;
        foreach (var item in items)
        {
            count++;
            await CleanEntityAsync(collection, item);
        }

        sw.Stop();
        if (count == 0)
        {
            _logger?.LogTrace($"Nothing to clean in collection {{collection}} in {{repositoryType}}. [action: Database, operation: {nameof(CleanAsync)}]", ProtectedCollectionName, "DiskRepository");
            InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(CleanAsync), Message = "Nothing to clean.", Level = LogLevel.Trace });
        }
        else
        {
            _logger?.LogInformation($"Cleaned {{count}} of {{totalCount}} took {{elapsed}} ms in collection {{collection}} in {{repositoryType}}. [action: Database, operation: {nameof(CleanAsync)}]", count, totalCount, sw.Elapsed.TotalMilliseconds, ProtectedCollectionName, "DiskRepository");
            InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(CleanAsync), Message = "Cleaned completed.", ItemCount = count, Elapsed = sw.Elapsed });
        }
    }

    internal override async Task DropEmpty(IMongoCollection<TEntity> collection)
    {
        if (!DropEmptyCollections) return;

        var any = await collection.CountDocumentsAsync(x => true, new CountOptions { Limit = 1 });
        if (any != 0) return;

        await DropCollectionAsync();
    }

    private async Task UpdateIndiciesAsync(IMongoCollection<TEntity> collection)
    {
        if (Indicies == null) return;

        if (Indicies.Any(x => string.IsNullOrEmpty(x.Options.Name))) throw new InvalidOperationException("Indicies needs to have a name.");

        //NOTE: Drop indexes not in list
        var indicies = (await collection.Indexes.ListAsync()).ToList();
        foreach (var index in indicies)
        {
            var indexName = index.GetValue("name").AsString;
            if (!indexName.StartsWith("_id_"))
            {
                if (Indicies.All(x => x.Options.Name != indexName))
                {
                    await collection.Indexes.DropOneAsync(indexName);
                }
            }
        }

        //NOTE: Create indexes in the list
        foreach (var index in Indicies)
        {
            await collection.Indexes.CreateOneAsync(index);
        }
    }

    internal override async Task<TEntity> CleanEntityAsync(TEntity item)
    {
        return await CleanEntityAsync(Collection, item);
    }

    private async Task<T> CleanEntityAsync<T>(IMongoCollection<T> collection, T item) where T : TEntity
    {
        if (item == null) return null;

        if (item.NeedsCleaning())
        {
            if (AutoClean)
            {
                var filter = Builders<T>.Filter.Eq(x => x.Id, item.Id);
                await collection.FindOneAndReplaceAsync(filter, item);
                _logger?.LogInformation($"Entity {{id}} of type {{entityType}} in collection {{collection}} has been cleaned. [action: Database, operation: {nameof(CleanEntityAsync)}]", item.Id, typeof(TEntity), ProtectedCollectionName);
                InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(CleanEntityAsync), Message = "Entity cleaned.", Data = new Dictionary<string, object> { { "id", item.Id } } });
            }
            else
            {
                _logger?.LogWarning($"Entity {{id}} of type {{entityType}} in collection {{collection}} needs cleaning. [action: Database, operation: {nameof(CleanEntityAsync)}]", item.Id, typeof(TEntity), ProtectedCollectionName);
                InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(CleanEntityAsync), Message = "Entity needs cleaning.", Level = LogLevel.Warning, Data = new Dictionary<string, object> { { "id", item.Id } } });
            }
        }

        return item;
    }
}
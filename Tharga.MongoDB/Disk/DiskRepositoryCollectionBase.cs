using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB.Disk;

public abstract class DiskRepositoryCollectionBase<TEntity, TKey> : RepositoryCollectionBase<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IMongoCollection<TEntity> _collection;

    protected DiskRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger = null, DatabaseContext databaseContext = null)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }

    private IMongoCollection<TEntity> Collection => _collection ??= Task.Run(async () => await FetchCollectionAsync()).Result;

    protected virtual async Task<T> Execute<T>(string functionName, Func<Task<T>> action, bool assureIndex)
    {
        var sw = new Stopwatch();
        sw.Start();

        try
        {
            if (assureIndex)
            {
                await AssureIndex(Collection);
            }

            var result = await action.Invoke();

            sw.Stop();

            _logger?.LogInformation($"Executed {{repositoryType}} took {{elapsed}} ms. [action: Database, operation: {functionName}]", "DiskRepository", sw.Elapsed.TotalMilliseconds);
            InvokeAction(new ActionEventArgs.ActionData { Operation = functionName, Elapsed = sw.Elapsed });

            return result;
        }
        catch (Exception e)
        {
            _logger?.LogError(e, $"Exception {{repositoryType}}. [action: Database, operation: {functionName}]", "DiskRepository");
            InvokeAction(new ActionEventArgs.ActionData { Operation = functionName, Exception = e });
            throw;
        }
    }

    public override async IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate, Options<TEntity> options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = new Stopwatch();
        sw.Start();

        var o = options == null ? null : new FindOptions<TEntity, TEntity> { Projection = options.Projection, Sort = options.Sort, Limit = options.Limit };
        var cursor = await FindAsync(Collection, predicate, cancellationToken, o);

        var count = 0;
        await foreach (var item in BuildList(cursor, cancellationToken).WithCancellation(cancellationToken))
        {
            count++;
            yield return item;
        }

        sw.Stop();
        _logger?.LogInformation($"Executed {{repositoryType}} took {{elapsed}} ms and returned {{itemCount}} items. [action: Database, operation: {nameof(GetAsync)}]", "DiskRepository", sw.Elapsed.TotalMilliseconds, count);
        InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(GetAsync), Elapsed = sw.Elapsed, ItemCount = count });
    }

    public override async IAsyncEnumerable<ResultPage<TEntity, TKey>> GetPageAsync(Expression<Func<TEntity, bool>> predicate, Options<TEntity> options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (ResultLimit == null) throw new InvalidOperationException("Cannot use GetPageAsync when no result limit has been configured.");
        if (ResultLimit <= 0) throw new InvalidOperationException("GetPageAsync has to be a number greater than 0.");

        var sw = new Stopwatch();
        sw.Start();

        var totalCount = (int)await Collection.CountDocumentsAsync(predicate, new CountOptions(), cancellationToken);
        var pages = (int)Math.Ceiling(totalCount / (decimal)ResultLimit.Value);
        if (options?.Limit != null && options.Limit < pages)
        {
            pages = options.Limit.Value;
        }

        for (var i = 0; i < pages; i++)
        {
            var skip = i * ResultLimit.Value;

            var o = options == null
                ? new FindOptions<TEntity, TEntity> { Limit = ResultLimit, Skip = skip }
                : new FindOptions<TEntity, TEntity> { Projection = options.Projection, Sort = options.Sort, Limit = ResultLimit, Skip = skip };
            var cursor = await FindAsync(Collection, predicate, cancellationToken, o);

            yield return new ResultPage<TEntity, TKey>
            {
                Items = BuildList(cursor, cancellationToken),
                TotalCount = totalCount,
                Page = i,
                TotalPages = pages
            };
        }

        sw.Stop();
        _logger?.LogInformation($"Executed {{repositoryType}} took {{elapsed}} ms and returned {{itemCount}} items on {{pages}} pages. [action: Database, operation: {nameof(GetPageAsync)}]", "DiskRepository", sw.Elapsed.TotalMilliseconds, totalCount, pages);
        InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(GetPageAsync), Elapsed = sw.Elapsed, ItemCount = totalCount, Data = new Dictionary<string, object> { { "pages", pages } } });
    }

    private async Task<IAsyncCursor<TEntity>> FindAsync(IMongoCollection<TEntity> collection, Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken, FindOptions<TEntity, TEntity> options)
    {
        IAsyncCursor<TEntity> cursor;
        try
        {
            cursor = await collection.FindAsync(predicate, options, cancellationToken);
        }
        catch (Exception e)
        {
            _logger?.LogError(e, $"Exception {{repositoryType}}. [action: Database, operation: {nameof(FindAsync)}]", "DiskRepository");
            InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(FindAsync), Exception = e });
            throw;
        }

        return cursor;
    }

    private async IAsyncEnumerable<TEntity> BuildList(IAsyncCursor<TEntity> cursor, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var index = 0;
        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var current in cursor.Current)
            {
                index++;
                if (ResultLimit != null && index > ResultLimit)
                {
                    throw new ResultLimitException(ResultLimit.Value);
                }

                yield return await CleanEntityAsync(current);
            }
        }
    }

    public override async Task<TEntity> GetOneAsync(TKey id, CancellationToken cancellationToken = default)
    {
        return await Execute(nameof(GetOneAsync), async () =>
        {
            var filter = Builders<TEntity>.Filter.Eq(x => x.Id, id);
            var item = await Collection.Find(filter).Limit(1).SingleOrDefaultAsync(cancellationToken);
            return await CleanEntityAsync(item);
        }, false);
    }

    public override async Task<TEntity> GetOneAsync(Expression<Func<TEntity, bool>> predicate, SortDefinition<TEntity> sort = default, CancellationToken cancellationToken = default)
    {
        return await Execute(nameof(GetOneAsync), async () =>
        {
            var item = await Collection.Find(predicate).Sort(sort).Limit(1).SingleOrDefaultAsync(cancellationToken);
            return await CleanEntityAsync(item);
        }, false);
    }

    public override async Task<T> GetOneAsync<T>(Expression<Func<T, bool>> predicate = null, SortDefinition<T> sort = default, CancellationToken cancellationToken = default)
    {
        return await Execute(nameof(GetOneAsync), async () =>
        {
            var typeFilter = Builders<T>.Filter.And(Builders<T>.Filter.OfType<T>(), new ExpressionFilterDefinition<T>(predicate ?? (_ => true)));
            var collection = _mongoDbService.GetCollection<T>(ProtectedCollectionName);
            var item = await collection.Find(typeFilter).Sort(sort).Limit(1).SingleOrDefaultAsync(cancellationToken);
            return item;
        }, false);
    }

    public override async Task<bool> AddAsync(TEntity entity)
    {
        return await Execute(nameof(AddAsync), async () =>
        {
            if (await DoesExistAsync(x => x.Id.Equals(entity.Id))) return false;
            await Collection.InsertOneAsync(entity);
            return true;
        }, true);
    }

    public override async Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity)
    {
        return await Execute(nameof(AddOrReplaceAsync), async () =>
        {
            var filter = Builders<TEntity>.Filter.Eq(x => x.Id, entity.Id);
            var current = await Collection.FindOneAndReplaceAsync(filter, entity);
            TEntity before = default;
            if (current == default)
            {
                await Collection.InsertOneAsync(entity);
                //data.AddField("operation", "inserted");
            }
            else
            {
                before = current;
                //data.AddField("operation", "updated");
            }

            return new EntityChangeResult<TEntity>(before, entity);
        }, true);
    }

    public override async Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update)
    {
        return await Execute(nameof(UpdateOneAsync), async () =>
        {
            var filter = Builders<TEntity>.Filter.Eq(x => x.Id, id);
            var options = new FindOneAndUpdateOptions<TEntity> { ReturnDocument = ReturnDocument.Before };
            var before = await Collection.FindOneAndUpdateAsync(filter, update, options);
            if (before == null) return new EntityChangeResult<TEntity>(default, default(TEntity));
            return new EntityChangeResult<TEntity>(before, async () =>
            {
                return await Collection.Find(x => x.Id.Equals(id)).SingleAsync();
            });
        }, true);
    }

    public override async Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, FindOneAndUpdateOptions<TEntity> options = default)
    {
        return await Execute(nameof(UpdateOneAsync), async () =>
        {
            options ??= new FindOneAndUpdateOptions<TEntity> { ReturnDocument = ReturnDocument.Before };
            if (options.ReturnDocument != ReturnDocument.Before) throw new InvalidOperationException($"The ReturnDocument option has to be set to {ReturnDocument.Before}. To get the '{ReturnDocument.After}', call method '{nameof(EntityChangeResult<TEntity>.GetAfterAsync)}()' on the result.");
            var before = await Collection.FindOneAndUpdateAsync(filter, update, options);
            if (before == null) return new EntityChangeResult<TEntity>(default, default(TEntity));
            return new EntityChangeResult<TEntity>(before, async () =>
            {
                return await Collection.Find(x => x.Id.Equals(before.Id)).SingleAsync();
            });
        }, true);
    }

    public override async Task<TEntity> DeleteOneAsync(TKey id)
    {
        return await Execute(nameof(DeleteOneAsync), async () =>
        {
            var filter = Builders<TEntity>.Filter.Eq(x => x.Id, id);
            var item = await Collection.FindOneAndDeleteAsync(filter);
            await DropEmpty(Collection);
            return item;
        }, false);
    }

    public override async Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate, FindOneAndDeleteOptions<TEntity, TEntity> options = default)
    {
        return await Execute(nameof(DeleteOneAsync), async () =>
        {
            var item = await Collection.FindOneAndDeleteAsync(predicate, options);
            await DropEmpty(Collection);
            return item;
        }, false);
    }

    public override async Task<DeleteResult> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate)
    {
        return await Execute(nameof(DeleteManyAsync), async () =>
        {
            var item = await Collection.DeleteManyAsync(predicate);
            await DropEmpty(Collection);
            return item;
        }, false);
    }

    public override async Task DropCollectionAsync()
    {
        await Execute(nameof(DropCollectionAsync), async () =>
        {
            await _mongoDbService.DropCollectionAsync(ProtectedCollectionName);
            return true;
        }, false);
    }

    public override async Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate)
    {
        return await Execute(nameof(CountAsync), async () =>
        {
            var count = await Collection.CountDocumentsAsync(predicate);
            return count;
        }, false);
    }

    public override async Task<long> GetSizeAsync()
    {
        return await Execute(nameof(GetSizeAsync), () => Task.FromResult(_mongoDbService.GetSize(ProtectedCollectionName)), false);
    }

    private async Task<IMongoCollection<TEntity>> FetchCollectionAsync()
    {
        return await Execute(nameof(FetchCollectionAsync), async () =>
        {
            try
            {
                await _lock.WaitAsync();

                var collection = _mongoDbService.GetCollection<TEntity>(ProtectedCollectionName);
                var exists = _mongoDbService.DoesCollectionExist(ProtectedCollectionName);

                if (InitiationLibrary.ShouldInitiate(ServerName, DatabaseName, ProtectedCollectionName))
                {
                    _logger?.LogTrace($"Starting to initiate {{collection}}. [action: Database, operation: {nameof(FetchCollectionAsync)}]", ProtectedCollectionName);
                    InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(FetchCollectionAsync), Message = "Starting to initiate.", Level = LogLevel.Trace });
                    RegisterTypes();

                    if (exists)
                    {
                        await AssureIndex(collection);
                        await CleanAsync(collection);
                        await DropEmpty(collection);
                    }

                    await InitAsync(collection);
                    _logger?.LogTrace($"Initiate {{collection}} is completed. [action: Database, operation: {nameof(FetchCollectionAsync)}]", ProtectedCollectionName);
                    InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(FetchCollectionAsync), Message = "Initiation completed.", Level = LogLevel.Trace });
                }
                else
                {
                    _logger?.LogTrace($"Skip initiation of {{collection}} because it has already been initiated. [action: Database, operation: {nameof(FetchCollectionAsync)}]", ProtectedCollectionName);
                    InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(FetchCollectionAsync), Message = "Skip initiation because it has already been completed.", Level = LogLevel.Trace });
                }

                return collection;
            }
            finally
            {
                _lock.Release();
            }
        }, false);
    }

    private async Task AssureIndex(IMongoCollection<TEntity> collection)
    {
        if (InitiationLibrary.ShouldInitiateIndex(ServerName, DatabaseName, ProtectedCollectionName))
        {
            await collection.Indexes.CreateOneAsync(new CreateIndexModel<TEntity>(Builders<TEntity>.IndexKeys.Ascending(x => x.Id).Ascending("_t"), new CreateIndexOptions()));
            await UpdateIndiciesAsync(collection);
        }
    }

    private void RegisterTypes()
    {
        if ((typeof(TEntity).IsInterface || typeof(TEntity).IsAbstract) && !Types.Any())
        {
            var kind = typeof(TEntity).IsInterface ? "an interface" : "an abstract class";
            throw new InvalidOperationException($"Types has to be provided since '{typeof(TEntity).Name}' it is {kind}. Do this by overriding the the Types property in '{GetType().Name}'.");
        }

        foreach (var type in Types ?? Array.Empty<Type>())
        {
            if (!BsonClassMap.IsClassMapRegistered(type))
            {
                var cm = new BsonClassMap(type);
                cm.AutoMap();
                BsonClassMap.RegisterClassMap(cm);
            }
        }
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

    private async Task<bool> DoesExistAsync(Expression<Func<TEntity, bool>> expression, CancellationToken cancellationToken = default)
    {
        var existing = await Collection.CountDocumentsAsync(expression, null, cancellationToken);
        return existing > 0;
    }

    protected virtual async Task CleanAsync(IMongoCollection<TEntity> collection)
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

        var cursor = await FindAsync(collection, x => true, CancellationToken.None, null);
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

    protected async Task DropEmpty(IMongoCollection<TEntity> collection)
    {
        if (!DropEmptyCollections) return;

        var any = await collection.CountDocumentsAsync(x => true, new CountOptions { Limit = 1 });
        if (any != 0) return;

        await DropCollectionAsync();
    }

    protected virtual async Task<TEntity> CleanEntityAsync(TEntity item)
    {
        return await CleanEntityAsync(Collection, item);
    }

    private async Task<TEntity> CleanEntityAsync(IMongoCollection<TEntity> collection, TEntity item)
    {
        if (item == null) return null;

        if (item.NeedsCleaning())
        {
            if (AutoClean)
            {
                var filter = Builders<TEntity>.Filter.Eq(x => x.Id, item.Id);
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
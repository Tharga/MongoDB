﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB.Disk;

public abstract class DiskRepositoryCollectionBase<TEntity, TKey> : RepositoryCollectionBase<TEntity, TKey>, IDiskRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IMongoCollection<TEntity> _collection;

    protected DiskRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger = null, DatabaseContext databaseContext = null)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }

    private IMongoCollection<TEntity> Collection => _collection ??= Task.Run(async () =>
    {
        try
        {
            _lock.Wait();
            return await FetchCollectionAsync();
        }
        catch (TimeoutException e)
        {
            try
            {
                _logger.LogWarning(e, "Failed to get collection, trying to open firewall.");
                await AssureFirewallAccessAsync();
                return await FetchCollectionAsync();
            }
            catch (Exception exception)
            {
                Debugger.Break();
                _logger.LogError(exception, exception.Message);
                throw;
            }
        }
        catch (Exception e)
        {
            Debugger.Break();
            _logger.LogError(e, e.Message);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }).Result;

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
        catch (Exception e) when (e is MongoConnectionException || e is TimeoutException || e is MongoConnectionPoolPausedException)
        {
            _logger?.LogWarning(e, $"{e.GetType().Name} {{repositoryType}}. [action: Database, operation: {functionName}]", "DiskRepository");
            InvokeAction(new ActionEventArgs.ActionData { Operation = functionName, Exception = e });
            throw;
        }
        catch (Exception e)
        {
            _logger?.LogError(e, $"{e.GetType().Name} {{repositoryType}}. [action: Database, operation: {functionName}]", "DiskRepository");
            InvokeAction(new ActionEventArgs.ActionData { Operation = functionName, Exception = e });
            throw;
        }
    }

    public override async IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = new Stopwatch();
        sw.Start();

        var o = BuildOptions(options);
        var cursor = await FindAsync(Collection, predicate, cancellationToken, o);

        var count = 0;
        await foreach (var item in BuildList(cursor, cancellationToken))
        {
            count++;
            yield return item;
        }

        sw.Stop();
        _logger?.LogInformation($"Executed {{repositoryType}} took {{elapsed}} ms and returned {{itemCount}} items. [action: Database, operation: {nameof(GetAsync)}]", "DiskRepository", sw.Elapsed.TotalMilliseconds, count);
        InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(GetAsync), Elapsed = sw.Elapsed, ItemCount = count });
    }

    private static FindOptions<TEntity, TEntity> BuildOptions(Options<TEntity> options)
    {
        FindOptions<TEntity, TEntity> o = null;
        if (options != null)
        {
            o = new FindOptions<TEntity, TEntity> { Sort = options.Sort, Limit = options.Limit, Skip = options.Skip };
            if (options.Projection != null)
            {
                o.Projection = options.Projection;
            }
        }

        return o;
    }

    public override async IAsyncEnumerable<TEntity> GetAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = new Stopwatch();
        sw.Start();

        var o = options == null ? null : new FindOptions<TEntity, TEntity> { Projection = options.Projection, Sort = options.Sort, Limit = options.Limit, Skip = options.Skip };
        var cursor = await FindAsync(Collection, filter, cancellationToken, o);

        var count = 0;
        await foreach (var item in BuildList(cursor, cancellationToken))
        {
            count++;
            yield return item;
        }

        sw.Stop();
        _logger?.LogInformation($"Executed {{repositoryType}} took {{elapsed}} ms and returned {{itemCount}} items. [action: Database, operation: {nameof(GetAsync)}]", "DiskRepository", sw.Elapsed.TotalMilliseconds, count);
        InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(GetAsync), Elapsed = sw.Elapsed, ItemCount = count });
    }

    public override async IAsyncEnumerable<T> GetAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = new Stopwatch();
        sw.Start();

        var filter = Builders<T>.Filter.And(Builders<T>.Filter.OfType<T>(), new ExpressionFilterDefinition<T>(predicate ?? (_ => true)));
        var o = options == null ? null : new FindOptions<T, T> { Projection = options.Projection, Sort = options.Sort, Limit = options.Limit, Skip = options.Skip };

        _ = Collection ?? throw new InvalidOperationException("Unable to initiate collection.");

        var collection = await GetCollectionAsync<T>();
        var cursor = await collection.FindAsync(filter ?? FilterDefinition<T>.Empty, o, cancellationToken);

        var count = 0;
        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var current in cursor.Current)
            {
                count++;
                if (ResultLimit != null && count > ResultLimit)
                {
                    throw new ResultLimitException(ResultLimit.Value);
                }

                yield return await CleanEntityAsync(collection, current);
            }
        }

        sw.Stop();
        _logger?.LogInformation($"Executed {{repositoryType}} took {{elapsed}} ms and returned {{itemCount}} items. [action: Database, operation: {nameof(GetAsync)}]", "DiskRepository", sw.Elapsed.TotalMilliseconds, count);
        InvokeAction(new ActionEventArgs.ActionData { Operation = $"{nameof(GetAsync)}<{typeof(T).Name}>", Elapsed = sw.Elapsed, ItemCount = count });
    }

    public override async IAsyncEnumerable<ResultPage<TEntity, TKey>> GetPagesAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (ResultLimit == null) throw new InvalidOperationException("Cannot use GetPagesAsync when no result limit has been configured.");
        if (ResultLimit <= 0) throw new InvalidOperationException("GetPagesAsync has to be a number greater than 0.");
        if (options?.Skip != null) throw new NotImplementedException("Skip while using page has not yet been implemented.");

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
        _logger?.LogInformation($"Executed {{repositoryType}} took {{elapsed}} ms and returned {{itemCount}} items on {{pages}} pages. [action: Database, operation: {nameof(GetPagesAsync)}]", "DiskRepository", sw.Elapsed.TotalMilliseconds, totalCount, pages);
        InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(GetPagesAsync), Elapsed = sw.Elapsed, ItemCount = totalCount, Data = new Dictionary<string, object> { { "pages", pages } } });
    }

    private async Task<IAsyncCursor<TEntity>> FindAsync(IMongoCollection<TEntity> collection, FilterDefinition<TEntity> filter, CancellationToken cancellationToken, FindOptions<TEntity, TEntity> options)
    {
        IAsyncCursor<TEntity> cursor;
        try
        {
            cursor = await collection.FindAsync(filter ?? FilterDefinition<TEntity>.Empty, options, cancellationToken);
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

    public override Task<TEntity> GetOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        var filter = predicate == null ? FilterDefinition<TEntity>.Empty : new ExpressionFilterDefinition<TEntity>(predicate);
        return GetOneAsync(filter, options, cancellationToken);
    }

    public override async Task<TEntity> GetOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return await Execute(nameof(GetOneAsync), async () =>
        {
            var sort = options?.Sort;
            var findFluent = Collection.Find(filter).Sort(sort).Limit(2);
            TEntity item;
            switch (options?.Mode)
            {
                case null:
                case EMode.Single:
                    item = await findFluent.SingleOrDefaultAsync(cancellationToken: cancellationToken);
                    break;
                case EMode.First:
                    item = await findFluent.FirstOrDefaultAsync(cancellationToken: cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return await CleanEntityAsync(item);
        }, false);
    }

    public override async Task<T> GetOneAsync<T>(Expression<Func<T, bool>> predicate = null, OneOption<T> options = null, CancellationToken cancellationToken = default)
    {
        return await Execute($"{nameof(GetOneAsync)}<{typeof(T).Name}>", async () =>
        {
            var filter = Builders<T>.Filter.And(Builders<T>.Filter.OfType<T>(), new ExpressionFilterDefinition<T>(predicate ?? (_ => true)));

            _ = Collection ?? throw new InvalidOperationException("Unable to initiate collection.");

            var collection = await GetCollectionAsync<T>();
            var findFluent = collection.Find(filter).Sort(options?.Sort).Limit(2);
            T item;
            switch (options?.Mode)
            {
                case null:
                case EMode.Single:
                    item = await findFluent.SingleOrDefaultAsync(cancellationToken: cancellationToken);
                    break;
                case EMode.First:
                    item = await findFluent.FirstOrDefaultAsync(cancellationToken: cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return await CleanEntityAsync(collection, item);
        }, false);
    }

    protected virtual async Task<IMongoCollection<T>> GetCollectionAsync<T>()
    {
        return await _mongoDbService.GetCollectionAsync<T>(ProtectedCollectionName);
    }

    public override async Task AddAsync(TEntity entity)
    {
        await Execute(nameof(AddAsync), async () =>
        {
            await Collection.InsertOneAsync(entity);
            return true;
        }, true);
    }

    public override async Task<bool> TryAddAsync(TEntity entity)
    {
        return await Execute(nameof(TryAddAsync), async () =>
        {
            try
            {
                await Collection.InsertOneAsync(entity);
                return true;
            }
            catch (Exception e) //TODO: Catch explicit exception
            {
                Debugger.Break();
                Console.WriteLine(e);
                return false;
            }
        }, true);
    }

    public override async Task AddManyAsync(IEnumerable<TEntity> entities)
    {
        await Execute(nameof(AddManyAsync), async () =>
        {
            await Collection.InsertManyAsync(entities);
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

    public async Task<long> UpdateAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update)
    {
        return await Execute(nameof(UpdateAsync), async () =>
        {
            var result = await Collection.UpdateManyAsync(filter, update);
            return result.ModifiedCount;
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

    public override async Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate = null, FindOneAndDeleteOptions<TEntity, TEntity> options = default)
    {
        return await Execute(nameof(DeleteOneAsync), async () =>
        {
            var item = await Collection.FindOneAndDeleteAsync(predicate, options);
            await DropEmpty(Collection);
            return item;
        }, false);
    }

    public override async Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate)
    {
        return await Execute(nameof(DeleteManyAsync), async () =>
        {
            var item = await Collection.DeleteManyAsync(predicate);
            await DropEmpty(Collection);
            return item.DeletedCount;
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

    public override async Task<long> CountAsync(FilterDefinition<TEntity> filter)
    {
        return await Execute(nameof(CountAsync), async () =>
        {
            var count = await Collection.CountDocumentsAsync(filter);
            return count;
        }, false);
    }

    public override async Task<long> GetSizeAsync()
    {
        return await Execute(nameof(GetSizeAsync), () => Task.FromResult(_mongoDbService.GetSize(ProtectedCollectionName)), false);
    }

    private async Task<IMongoCollection<TEntity>> FetchCollectionAsync()
    {
        //return await Execute(nameof(FetchCollectionAsync), async () =>
        //{
        //    try
        //    {
        //        await _lock.WaitAsync();

                var collection = await GetCollectionAsync<TEntity>();

                if (InitiationLibrary.ShouldInitiate(ServerName, DatabaseName, ProtectedCollectionName))
                {
                    _logger?.LogTrace($"Starting to initiate {{collection}}. [action: Database, operation: {nameof(FetchCollectionAsync)}]", ProtectedCollectionName);
                    InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(FetchCollectionAsync), Message = "Starting to initiate.", Level = LogLevel.Trace });
                    RegisterTypes();

                    var exists = await _mongoDbService.DoesCollectionExist(ProtectedCollectionName);
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
        //    }
        //    finally
        //    {
        //        _lock.Release();
        //    }
        //}, false);
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
        if ((typeof(TEntity).IsInterface || typeof(TEntity).IsAbstract) && (Types == null || !Types.Any()))
        {
            var kind = typeof(TEntity).IsInterface ? "an interface" : "an abstract class";
            throw new InvalidOperationException($"Types has to be provided since '{typeof(TEntity).Name}' it is {kind}. Do this by overriding the the Types property in '{GetType().Name}' and provide the requested type.");
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

    protected virtual async Task DropEmpty(IMongoCollection<TEntity> collection)
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

public abstract class DiskRepositoryCollectionBase<TEntity> : DiskRepositoryCollectionBase<TEntity, ObjectId>
    where TEntity : EntityBase
{
    protected DiskRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TEntity, ObjectId>> logger = null, DatabaseContext databaseContext = null)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }
}
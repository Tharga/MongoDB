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

    /// <summary>
    /// Override this constructor for static collections.
    /// </summary>
    /// <param name="mongoDbServiceFactory"></param>
    /// <param name="logger"></param>
	protected DiskRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger = null)
	    : base(mongoDbServiceFactory, logger)
    {
    }

	/// <summary>
	/// Use this constructor for dynamic collections together with ICollectionProvider.
	/// </summary>
	/// <param name="mongoDbServiceFactory"></param>
	/// <param name="logger"></param>
	/// <param name="databaseContext"></param>
	protected DiskRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger, DatabaseContext databaseContext)
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

            _logger?.Log(_executeInfoLogLevel, $"Executed {{repositoryType}} for {{CollectionName}} took {{elapsed}} ms. [action: Database, operation: {functionName}]", "DiskRepository", CollectionName, sw.Elapsed.TotalMilliseconds);
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
        _logger?.Log(_executeInfoLogLevel, $"Executed {{repositoryType}} for {{CollectionName}} took {{elapsed}} ms and returned {{itemCount}} items. [action: Database, operation: {nameof(GetAsync)}]", "DiskRepository", CollectionName, sw.Elapsed.TotalMilliseconds, count);
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

        //var o = options == null ? null : new FindOptions<TEntity, TEntity> { Projection = options.Projection, Sort = options.Sort, Limit = options.Limit, Skip = options.Skip };
        var o = BuildOptions(options);
        var cursor = await FindAsync(Collection, filter, cancellationToken, o);

        var count = 0;
        await foreach (var item in BuildList(cursor, cancellationToken))
        {
            count++;
            yield return item;
        }

        sw.Stop();
        _logger?.Log(_executeInfoLogLevel, $"Executed {{repositoryType}} for {{CollectionName}} took {{elapsed}} ms and returned {{itemCount}} items. [action: Database, operation: {nameof(GetAsync)}]", "DiskRepository", CollectionName, sw.Elapsed.TotalMilliseconds, count);
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
        _logger?.Log(_executeInfoLogLevel, $"Executed {{repositoryType}} for {{CollectionName}} took {{elapsed}} ms and returned {{itemCount}} items. [action: Database, operation: {nameof(GetAsync)}]", "DiskRepository", CollectionName, sw.Elapsed.TotalMilliseconds, count);
        InvokeAction(new ActionEventArgs.ActionData { Operation = $"{nameof(GetAsync)}<{typeof(T).Name}>", Elapsed = sw.Elapsed, ItemCount = count });
    }

    public override async IAsyncEnumerable<T> GetProjectionAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = new Stopwatch();
        sw.Start();

        var filter = Builders<T>.Filter.And(new ExpressionFilterDefinition<T>(predicate ?? (_ => true)));
        var o = options == null
            ? new FindOptions<T, T>
            {
                Projection = BuildProjection<T>(),
            }
            : new FindOptions<T, T>
            {
                Projection = options.Projection ?? BuildProjection<T>(),
                Sort = options.Sort,
                Limit = options.Limit,
                Skip = options.Skip
            };

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

                yield return current;
            }
        }

        sw.Stop();
        _logger?.Log(_executeInfoLogLevel, $"Executed {{repositoryType}} for {{CollectionName}} took {{elapsed}} ms and returned {{itemCount}} items. [action: Database, operation: {nameof(GetAsync)}]", "DiskRepository", CollectionName, sw.Elapsed.TotalMilliseconds, count);
        InvokeAction(new ActionEventArgs.ActionData { Operation = $"{nameof(GetProjectionAsync)}<{typeof(T).Name}>", Elapsed = sw.Elapsed, ItemCount = count });

        //return await Execute($"{nameof(GetProjectionAsync)}<{typeof(T).Name}>", async () =>
        //{
        //    var filter = Builders<T>.Filter.And(new ExpressionFilterDefinition<T>(predicate ?? (_ => true)));

        //    var projection = BuildProjection<T>();

        //    var collection = await GetCollectionAsync<T>();
        //    //var result = collection.Find(filter).Sort(options?.Sort).Project(projection).Limit(2);
        //    //var findFluent = result.ToEnumerable().Select(x => BsonSerializer.Deserialize<T>(x)).ToAsyncEnumerable();

        //    //T item;
        //    //switch (options?.Mode)
        //    //{
        //    //    case null:
        //    //    case EMode.SingleOrDefault:
        //    //        item = await findFluent.SingleOrDefaultAsync(cancellationToken: cancellationToken);
        //    //        break;
        //    //    case EMode.Single:
        //    //        item = await findFluent.SingleAsync(cancellationToken: cancellationToken);
        //    //        break;
        //    //    case EMode.FirstOrDefault:
        //    //        item = await findFluent.FirstOrDefaultAsync(cancellationToken: cancellationToken);
        //    //        break;
        //    //    case EMode.First:
        //    //        item = await findFluent.FirstAsync(cancellationToken: cancellationToken);
        //    //        break;
        //    //    default:
        //    //        throw new ArgumentOutOfRangeException();
        //    //}

        //    //return item;
        //}, false);

    }

    public override async Task<Result<TEntity, TKey>> QueryAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        var sw = new Stopwatch();
        sw.Start();

        var o = BuildOptions(options);
        var totalCount = await Collection.CountDocumentsAsync(predicate ?? FilterDefinition<TEntity>.Empty, cancellationToken: cancellationToken);
        var cursor = await FindAsync(Collection, predicate, cancellationToken, o);

        var items = await BuildList(cursor, cancellationToken).ToArrayAsync(cancellationToken: cancellationToken);
        var count = items.Length;

        sw.Stop();
        _logger?.Log(_executeInfoLogLevel, $"Executed {{repositoryType}} for {{CollectionName}} took {{elapsed}} ms and returned {{itemCount}} items. [action: Database, operation: {nameof(QueryAsync)}]", "DiskRepository", CollectionName, sw.Elapsed.TotalMilliseconds, count);
        InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(GetAsync), Elapsed = sw.Elapsed, ItemCount = count });

        return new Result<TEntity, TKey>
        {
            Items = items,
            TotalCount = (int)totalCount
        };
    }

    public override async Task<Result<TEntity, TKey>> QueryAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        var sw = new Stopwatch();
        sw.Start();

        var o = BuildOptions(options);
        var totalCount = await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        var cursor = await FindAsync(Collection, filter, cancellationToken, o);

        var items = await BuildList(cursor, cancellationToken).ToArrayAsync(cancellationToken: cancellationToken);
        var count = items.Length;

        sw.Stop();
        _logger?.Log(_executeInfoLogLevel, $"Executed {{repositoryType}} for {{CollectionName}} took {{elapsed}} ms and returned {{itemCount}} items. [action: Database, operation: {nameof(QueryAsync)}]", "DiskRepository", CollectionName, sw.Elapsed.TotalMilliseconds, count);
        InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(GetAsync), Elapsed = sw.Elapsed, ItemCount = count });

        return new Result<TEntity, TKey>
        {
            Items = items,
            TotalCount = (int)totalCount
        };
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
        _logger?.Log(_executeInfoLogLevel, $"Executed {{repositoryType}} for {{CollectionName}} took {{elapsed}} ms and returned {{itemCount}} items on {{pages}} pages. [action: Database, operation: {nameof(GetPagesAsync)}]", "DiskRepository", CollectionName, sw.Elapsed.TotalMilliseconds, totalCount, pages);
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
                case EMode.SingleOrDefault:
                    item = await findFluent.SingleOrDefaultAsync(cancellationToken: cancellationToken);
                    break;
                case EMode.Single:
                    item = await findFluent.SingleAsync(cancellationToken: cancellationToken);
                    break;
                case EMode.FirstOrDefault:
                    item = await findFluent.FirstOrDefaultAsync(cancellationToken: cancellationToken);
                    break;
                case EMode.First:
                    item = await findFluent.FirstAsync(cancellationToken: cancellationToken);
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
            //var filter = Builders<T>.Filter.And(Builders<T>.Filter.OfType<T>(), new ExpressionFilterDefinition<T>(predicate ?? (_ => true)));
            var filter = Builders<T>.Filter.And(Builders<T>.Filter.Eq("_t", typeof(T).Name), new ExpressionFilterDefinition<T>(predicate ?? (_ => true)));

            _ = Collection ?? throw new InvalidOperationException("Unable to initiate collection.");

            var collection = await GetCollectionAsync<T>();
            var findFluent = collection.Find(filter).Sort(options?.Sort); //.Limit(2);
            T item;
            switch (options?.Mode)
            {
                case null:
                case EMode.SingleOrDefault:
                    item = await findFluent.SingleOrDefaultAsync(cancellationToken: cancellationToken);
                    break;
                case EMode.Single:
                    item = await findFluent.SingleAsync(cancellationToken: cancellationToken);
                    break;
                case EMode.FirstOrDefault:
                    item = await findFluent.FirstOrDefaultAsync(cancellationToken: cancellationToken);
                    break;
                case EMode.First:
                    item = await findFluent.FirstAsync(cancellationToken: cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return await CleanEntityAsync(collection, item);
        }, false);
    }

    public override async Task<T> GetOneProjectionAsync<T>(Expression<Func<T, bool>> predicate = null, OneOption<T> options = null, CancellationToken cancellationToken = default)
    {
        return await Execute($"{nameof(GetOneProjectionAsync)}<{typeof(T).Name}>", async () =>
        {
            var filter = Builders<T>.Filter.And(new ExpressionFilterDefinition<T>(predicate ?? (_ => true)));

            var projection = BuildProjection<T>();

            var collection = await GetCollectionAsync<T>();
            var result = collection.Find(filter).Sort(options?.Sort).Project(projection).Limit(2);
            var findFluent = result.ToEnumerable().Select(x => BsonSerializer.Deserialize<T>(x)).ToAsyncEnumerable();

            T item;
            switch (options?.Mode)
            {
                case null:
                case EMode.SingleOrDefault:
                    item = await findFluent.SingleOrDefaultAsync(cancellationToken: cancellationToken);
                    break;
                case EMode.Single:
                    item = await findFluent.SingleAsync(cancellationToken: cancellationToken);
                    break;
                case EMode.FirstOrDefault:
                    item = await findFluent.FirstOrDefaultAsync(cancellationToken: cancellationToken);
                    break;
                case EMode.First:
                    item = await findFluent.FirstAsync(cancellationToken: cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return item;
        }, false);
    }

    private static ProjectionDefinition<T> BuildProjection<T>()
    {
        var builder = new ProjectionDefinitionBuilder<T>();
        var props = typeof(T).GetProperties().ToArray();
        var projections = props.Select(x => Builders<T>.Projection.Include(x.Name));
        var projectionDefinition = builder.Combine(projections);
        return projectionDefinition;
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
            catch (MongoWriteException)
            {
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
            }
            else
            {
                before = current;
            }

            return new EntityChangeResult<TEntity>(before, entity);
        }, true);
    }

    public override Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, OneOption<TEntity> options = null)
    {
        var filter = Builders<TEntity>.Filter.Eq(x => x.Id, entity.Id);
        return ReplaceOneWithCheckAsync(entity, filter, options);
    }

    public override Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, FilterDefinition<TEntity> filter, OneOption<TEntity> options = null)
    {
        return ReplaceOneWithCheckAsync(entity, filter, options);
    }

    private async Task<EntityChangeResult<TEntity>> ReplaceOneWithCheckAsync(TEntity entity, FilterDefinition<TEntity> filter, OneOption<TEntity> options)
    {
        return await Execute(nameof(ReplaceOneAsync), async () =>
        {
            var sort = options?.Sort;
            var findFluent = Collection.Find(filter).Sort(sort).Limit(2);
            TEntity item;
            switch (options?.Mode)
            {
                case null:
                case EMode.SingleOrDefault:
                    item = await findFluent.SingleOrDefaultAsync();
                    break;
                case EMode.Single:
                    item = await findFluent.SingleAsync();
                    break;
                case EMode.FirstOrDefault:
                    item = await findFluent.FirstOrDefaultAsync();
                    break;
                case EMode.First:
                    item = await findFluent.FirstAsync();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (!item.Id.Equals(entity.Id)) throw new InvalidOperationException("Entity not covered by filter.");
            filter = Builders<TEntity>.Filter.Eq(x => x.Id, entity.Id);

            var before = await Collection.FindOneAndReplaceAsync(filter, entity);
            return new EntityChangeResult<TEntity>(before, entity);
        }, true);
    }

    public override async Task<long> UpdateAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update)
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

    [Obsolete("Use UpdateOneAsync with 'OneOption' instead.")]
    public override async Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, FindOneAndUpdateOptions<TEntity> options)
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

    public override async Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, OneOption<TEntity> options = default)
    {
        if (filter == null) throw new ArgumentException(nameof(filter));
        if (update == null) throw new ArgumentException(nameof(update));

        return await Execute(nameof(UpdateOneAsync), async () =>
        {
            var sort = options?.Sort;
            var findFluent = Collection.Find(filter).Sort(sort).Limit(2);
            TEntity item;
            switch (options?.Mode)
            {
                case null:
                case EMode.SingleOrDefault:
                    item = await findFluent.SingleOrDefaultAsync();
                    break;
                case EMode.Single:
                    item = await findFluent.SingleAsync();
                    break;
                case EMode.FirstOrDefault:
                    item = await findFluent.FirstOrDefaultAsync();
                    break;
                case EMode.First:
                    item = await findFluent.FirstAsync();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (item == null) return new EntityChangeResult<TEntity>(default, default(TEntity));

            var itemFilter = new FilterDefinitionBuilder<TEntity>().Eq(x => x.Id, item.Id);
            item = await Collection.FindOneAndUpdateAsync(itemFilter, update);

            return new EntityChangeResult<TEntity>(item, async () =>
            {
                return await Collection.Find(x => x.Id.Equals(item.Id)).SingleAsync();
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

    [Obsolete("Use DeleteOneAsync with 'OneOption' instead.")]
    public override async Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate, FindOneAndDeleteOptions<TEntity, TEntity> options)
    {
        return await Execute(nameof(DeleteOneAsync), async () =>
        {
            var item = await Collection.FindOneAndDeleteAsync(predicate, options);
            await DropEmpty(Collection);
            return item;
        }, false);
    }

    public override async Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate = default, OneOption<TEntity> options = default)
    {
        if (predicate == null) throw new ArgumentException(nameof(predicate));

        return await Execute(nameof(UpdateOneAsync), async () =>
        {
            var sort = options?.Sort;
            var findFluent = Collection.Find(predicate).Sort(sort).Limit(2);
            TEntity item;
            switch (options?.Mode)
            {
                case null:
                case EMode.SingleOrDefault:
                    item = await findFluent.SingleOrDefaultAsync();
                    break;
                case EMode.Single:
                    item = await findFluent.SingleAsync();
                    break;
                case EMode.FirstOrDefault:
                    item = await findFluent.FirstOrDefaultAsync();
                    break;
                case EMode.First:
                    item = await findFluent.FirstAsync();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (item == null) return default;

            var itemFilter = new FilterDefinitionBuilder<TEntity>().Eq(x => x.Id, item.Id);
            await Collection.FindOneAndDeleteAsync(itemFilter);
            await DropEmpty(Collection);
            return item;
        }, true);
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

    public override IMongoCollection<TEntity> GetCollection()
    {
        return Collection;
    }

    public override async Task DropCollectionAsync()
    {
        await Execute(nameof(DropCollectionAsync), async () =>
        {
            await _mongoDbService.DropCollectionAsync(ProtectedCollectionName);
            return true;
        }, false);
    }

    public override async Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await Execute(nameof(CountAsync), async () =>
        {
            var count = await Collection.CountDocumentsAsync(predicate, cancellationToken: cancellationToken);
            return count;
        }, false);
    }

    public override async Task<long> CountAsync(FilterDefinition<TEntity> filter, CancellationToken cancellationToken = default)
    {
        return await Execute(nameof(CountAsync), async () =>
        {
            var count = await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
            return count;
        }, false);
    }

    //public override async IAsyncEnumerable<TTarget> AggregateAsync<TTarget>(FilterDefinition<TEntity> filter, EPrecision precision, AggregateOperations<TTarget> operations, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    //{
    //    var serializerRegistry = BsonSerializer.SerializerRegistry;
    //    var documentSerializer = serializerRegistry.GetSerializer<TEntity>();
    //    var renderedFilter = filter.Render(documentSerializer, serializerRegistry);

    //    var properties = AggregatePropertyCache.GetProperties<TEntity, TTarget>().ToArray();

    //    var timestampGrouping = BuildTimestampGrouping(precision);
    //    timestampGrouping.AddRange(properties.ToDictionary(x => x, x => $"${x}"));

    //    var group = new BsonDocument
    //    {
    //        { "_id", timestampGrouping },
    //        { "Time", new BsonDocument("$first", "$Timestamp") },
    //    };
    //    group.AddRange(properties.ToDictionary(x => x, x => new BsonDocument("$first", $"${x}")));
    //    group.AddRange(operations.Build());

    //    var pipeline = new[] { new BsonDocument("$match", renderedFilter), new BsonDocument("$group", group) };
    //    var result = await Collection.AggregateAsync<TTarget>(pipeline, cancellationToken: cancellationToken);

    //    await foreach (var item in AddTimeInfo(result, precision, cancellationToken))
    //    {
    //        yield return item;
    //    }
    //}

    //private static BsonDocument BuildTimestampGrouping(EPrecision precision)
    //{
    //    switch (precision)
    //    {
    //        case EPrecision.Second:
    //            return new BsonDocument
    //            {
    //                { "second", new BsonDocument("second", "$Timestamp") },
    //                { "minute", new BsonDocument("$minute", "$Timestamp") },
    //                { "hour", new BsonDocument("$hour", "$Timestamp") },
    //                { "dayOfMonth", new BsonDocument("$dayOfMonth", "$Timestamp") },
    //                { "month", new BsonDocument("$month", "$Timestamp") },
    //                { "year", new BsonDocument("$year", "$Timestamp") },
    //            };
    //        case EPrecision.Minute:
    //            return new BsonDocument
    //            {
    //                { "minute", new BsonDocument("$minute", "$Timestamp") },
    //                { "hour", new BsonDocument("$hour", "$Timestamp") },
    //                { "dayOfMonth", new BsonDocument("$dayOfMonth", "$Timestamp") },
    //                { "month", new BsonDocument("$month", "$Timestamp") },
    //                { "year", new BsonDocument("$year", "$Timestamp") },
    //            };
    //        case EPrecision.Hour:
    //            return new BsonDocument
    //            {
    //                { "hour", new BsonDocument("$hour", "$Timestamp") },
    //                { "dayOfMonth", new BsonDocument("$dayOfMonth", "$Timestamp") },
    //                { "month", new BsonDocument("$month", "$Timestamp") },
    //                { "year", new BsonDocument("$year", "$Timestamp") },
    //            };
    //        case EPrecision.Day:
    //            return new BsonDocument
    //            {
    //                { "dayOfMonth", new BsonDocument("$dayOfMonth", "$Timestamp") },
    //                { "month", new BsonDocument("$month", "$Timestamp") },
    //                { "year", new BsonDocument("$year", "$Timestamp") },
    //            };
    //        case EPrecision.Month:
    //            return new BsonDocument
    //            {
    //                { "month", new BsonDocument("$month", "$Timestamp") },
    //                { "year", new BsonDocument("$year", "$Timestamp") },
    //            };
    //        case EPrecision.Year:
    //            return new BsonDocument
    //            {
    //                { "year", new BsonDocument("$year", "$Timestamp") },
    //            };
    //        default:
    //            throw new ArgumentOutOfRangeException(nameof(precision), precision, null);
    //    }
    //}

    //private async IAsyncEnumerable<TTarget> AddTimeInfo<TTarget>(IAsyncCursor<TTarget> cursor, EPrecision precision, [EnumeratorCancellation] CancellationToken cancellationToken)
    //    where TTarget : TimeEntityBase
    //{
    //    while (await cursor.MoveNextAsync(cancellationToken))
    //    {
    //        var batch = cursor.Current;
    //        foreach (var item in batch)
    //        {
    //            yield return item with { Time = TrunkateTime(item.Time, precision) };
    //        }
    //    }
    //}

    //private DateTime TrunkateTime(DateTime itemTime, EPrecision precision)
    //{
    //    itemTime = itemTime.AddMilliseconds(-itemTime.Millisecond);

    //    if (precision >= EPrecision.Minute)
    //    {
    //        itemTime = itemTime.AddSeconds(-itemTime.Second);
    //    }

    //    if (precision >= EPrecision.Hour)
    //    {
    //        itemTime = itemTime.AddMinutes(-itemTime.Minute);
    //    }

    //    if (precision >= EPrecision.Day)
    //    {
    //        itemTime = itemTime.AddHours(-itemTime.Hour);
    //    }

    //    if (precision >= EPrecision.Month)
    //    {
    //        itemTime = itemTime.AddDays(-itemTime.Day);
    //    }

    //    if (precision >= EPrecision.Year)
    //    {
    //        itemTime = itemTime.AddMonths(-itemTime.Month);
    //    }

    //    return itemTime;
    //}

    public override async Task<long> GetSizeAsync()
    {
        return await Execute(nameof(GetSizeAsync), () => Task.FromResult(_mongoDbService.GetSize(ProtectedCollectionName)), false);
    }

    private async Task<IMongoCollection<TEntity>> FetchCollectionAsync()
    {
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
    }

    private async Task AssureIndex(IMongoCollection<TEntity> collection)
    {
        if (InitiationLibrary.ShouldInitiateIndex(ServerName, DatabaseName, ProtectedCollectionName))
        {
            _logger?.LogTrace($"Assure index for collection {{collection}} in {{repositoryType}}. [action: Database, operation: {nameof(CleanAsync)}]", ProtectedCollectionName, "DiskRepository");

            await collection.Indexes.CreateOneAsync(new CreateIndexModel<TEntity>(Builders<TEntity>.IndexKeys.Ascending(x => x.Id).Ascending("_t"), new CreateIndexOptions()));
            await UpdateIndicesAsync(collection);
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

    private async Task UpdateIndicesAsync(IMongoCollection<TEntity> collection)
    {
        var indices = (CoreIndices?.ToArray() ?? Array.Empty<CreateIndexModel<TEntity>>()).Union(Indices?.ToArray() ?? Array.Empty<CreateIndexModel<TEntity>>()).ToArray();
        if (!indices.Any()) return;

        var firstInvalid = indices.GroupBy(x => x.Options.Name).FirstOrDefault(x => x.Count() > 1);
        if (firstInvalid != null)
            throw new InvalidOperationException($"Indices can only be defined once with the same name. Index {firstInvalid.First().Options.Name} has been defined {firstInvalid.Count()} times for collection {ProtectedCollectionName}.");

        if (indices.Any(x => string.IsNullOrEmpty(x.Options.Name)))
            throw new InvalidOperationException("Indices needs to have a name.");

        var allExistingIndexNames = (await collection.Indexes.ListAsync()).ToList()
            .Select(x => x.GetValue("name").AsString)
            .ToArray();
        var existingIndexNames = allExistingIndexNames
            .Where(x => !x.StartsWith("_id_"))
            .ToArray();

        _logger?.Log(_executeInfoLogLevel, "Assure index for collection {collection} with {count} documents.", ProtectedCollectionName, await collection.CountDocumentsAsync(x => true));
        _logger?.LogTrace("All existing indices in collection {collection}: {indices}.", ProtectedCollectionName, string.Join(", ", allExistingIndexNames));
        _logger?.LogDebug("Considered existing indices in collection {collection}: {indices}.", ProtectedCollectionName, string.Join(", ", existingIndexNames));
        _logger?.LogDebug("Defined indices for collection {collection}: {indices}.", ProtectedCollectionName, string.Join(", ", indices.Select(x => x.Options.Name)));

        //NOTE: Drop indexes not in list
        foreach (var indexName in existingIndexNames)
        {
            if (indices.All(x => x.Options.Name != indexName))
            {
                try
                {
                    await collection.Indexes.DropOneAsync(indexName);
                    _logger?.LogInformation("Index {indexName} was dropped in collection {collection}.", indexName, ProtectedCollectionName);
                }
                catch (Exception e)
                {
                    Debugger.Break();
                    _logger?.LogError(e, "Failed to drop index {indexName} in collection {collection}. {message}", indexName, ProtectedCollectionName, e.Message);
                }
            }
        }

        //NOTE: Create indexes in the list
        foreach (var index in indices)
        {
            if (existingIndexNames.All(x => index.Options.Name != x))
            {
                try
                {
                    var message = await collection.Indexes.CreateOneAsync(index);
                    _logger?.LogInformation("Index {indexName} was created in collection {collection}. {message}", index.Options.Name, ProtectedCollectionName, message);
                }
                catch (Exception e)
                {
                    Debugger.Break();
                    _logger?.LogError(e, "Failed to create index {indexName} in collection {collection}. {message}", index.Options.Name, ProtectedCollectionName, e.Message);
                }
            }
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

        try
        {
            _logger?.LogTrace($"Starting to clean in collection {{collection}} in {{repositoryType}}. [action: Database, operation: {nameof(CleanAsync)}]", ProtectedCollectionName, "DiskRepository");

            using var cursor = await FindAsync(collection, filter, CancellationToken.None, null);
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
        catch (FormatException)
        {
            _logger?.LogError("Failed to clean collection {collection} in {repositoryType}.", ProtectedCollectionName, "DiskRepository");
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
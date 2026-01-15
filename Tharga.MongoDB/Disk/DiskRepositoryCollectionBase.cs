using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB.Disk;

public abstract class DiskRepositoryCollectionBase<TEntity, TKey> : RepositoryCollectionBase<TEntity, TKey>, IDiskRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    private readonly IExecuteLimiter _databaseExecutor;
    private readonly ICollectionPool _collectionPool;
    private readonly IInitiationLibrary _initiationLibrary;
    private readonly SemaphoreSlim _fetchLock = new(1, 1);

    //TODO: Go over NotImplementedException, write unit tests and implement.

    /// <summary>
    /// Override this constructor for static collections.
    /// </summary>
    /// <param name="mongoDbServiceFactory"></param>
    /// <param name="logger"></param>
	protected DiskRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger = null)
	    : this(mongoDbServiceFactory, logger, null)
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
        _databaseExecutor = ((MongoDbService)_mongoDbService).ExecuteLimiter;
        _collectionPool = ((MongoDbService)_mongoDbService).CollectionPool;
        _initiationLibrary = ((MongoDbService)_mongoDbService).InitiationLibrary;
    }

    protected virtual async Task<T> ExecuteAsync<T>(string functionName, Func<IMongoCollection<TEntity>, CancellationToken, Task<(T Data, int Count)>> action, Operation operation, CancellationToken cancellationToken = default)
    {
        var callKey = Guid.NewGuid();
        var startAt = Stopwatch.GetTimestamp();
        var steps = new List<StepResponse>();
        var count = -1;

        //NOTE: Register call started, in monitor
        steps.Add(FireCallStartEvent(functionName, operation, callKey));

        Exception exception = null;
        try
        {
            var response = await _databaseExecutor.ExecuteAsync(async ct =>
            {
                steps.Add(new StepResponse { Timestamp = Stopwatch.GetTimestamp(), Step = "Queue" });

                //NOTE: Fetch collection
                var fetchCollectionStep = await FetchCollectionAsync();
                steps.Add(fetchCollectionStep);
                var collection = fetchCollectionStep.Value;

                //NOTE: Handle index depending on operation
                steps.Add(await OperationIndexManagement(operation, collection));

                //NOTE: Perform action
                var response = await action.Invoke(collection, ct);
                steps.Add(new StepResponse { Timestamp = Stopwatch.GetTimestamp(), Step = "Action" });

                if (operation == Operation.Delete) await DropEmptyAsync(collection);

                //TODO: Option to turn on explain mode. This will be the analysis steps.
                //TODO: Try to get more information about the filter or predicate on the call.

                return response;
            }, $"MongoDB.{ConfigurationName ?? Constants.DefaultConfigurationName}", cancellationToken);

            count = response.Result.Count;
            return response.Result.Data;
        }
        catch (Exception e) when (e is MongoConnectionException || e is TimeoutException || e is MongoConnectionPoolPausedException)
        {
            exception = e;
            _logger?.LogWarning(e, $"{e.GetType().Name} {{repositoryType}}. [action: Database, operation: {functionName}]", "DiskRepository");
            InvokeAction(new ActionEventArgs.ActionData { Operation = functionName, Exception = e });
            throw;
        }
        catch (Exception e)
        {
            exception = e;
            _logger?.LogError(e, $"{e.GetType().Name} {{repositoryType}}. [action: Database, operation: {functionName}]", "DiskRepository");
            InvokeAction(new ActionEventArgs.ActionData { Operation = functionName, Exception = e });
            throw;
        }
        finally
        {
            //NOTE: Finalize
            var elapsed = GetElapsed(startAt, Stopwatch.GetTimestamp());

            var total = TimeSpan.Zero;
            var info = steps.Select((x, index) =>
            {
                var from = index == 0 ? startAt : steps[index - 1].Timestamp;
                var delta = GetElapsed(from, x.Timestamp);
                total = total.Add(delta);
                return $"{x.Step}: {delta.TotalMilliseconds}";
            });
            var ss = string.Join(", ", info);

            ((MongoDbServiceFactory)_mongoDbServiceFactory).OnCallEnd(this, new CallEndEventArgs(callKey, elapsed, exception, count));
            //_logger?.LogInformation("Executed {method} on {collection} took {elapsed} ms. [{steps}, overhead: {overhead}]", functionName, CollectionName, elapsed.TotalMilliseconds, ss, (elapsed - total).TotalMilliseconds);
            var data = new Dictionary<string, object>
            {
                { "Monitor", "MongoDB" },
                { "Method", "Measure" },
            };

            var details = System.Text.Json.JsonSerializer.Serialize(data);
            _logger?.LogInformation("Measured {Action} in {Elapsed} ms. {Details} [{steps}, overhead: {overhead}]", $"MongoDB.{CollectionName}.{functionName}", elapsed, details, ss, (elapsed - total).TotalMilliseconds);
        }
    }

    private StepResponse FireCallStartEvent(string functionName, Operation operation, Guid callKey)
    {
        var fingerprint = new CollectionFingerprint { ConfigurationName = ConfigurationName ?? Constants.DefaultConfigurationName, DatabaseName = DatabaseName, CollectionName = CollectionName };
        ((MongoDbServiceFactory)_mongoDbServiceFactory).OnCallStart(this, new CallStartEventArgs(callKey, fingerprint, functionName, operation));

        return new StepResponse
        {
            Timestamp = Stopwatch.GetTimestamp(),
            Step = nameof(FireCallStartEvent)
        };
    }

    private async Task<StepResponse> OperationIndexManagement(Operation operation, IMongoCollection<TEntity> collection)
    {
        switch (operation)
        {
            case Operation.Read:
                break;
            case Operation.Create:
                await AssureIndex(collection);
                break;
            case Operation.Update:
                await AssureIndex(collection);
                ArmRecheckInvalidIndex();
                break;
            case Operation.Delete:
                ArmRecheckInvalidIndex();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }

        return new StepResponse
        {
            Timestamp = Stopwatch.GetTimestamp(),
            Step = nameof(OperationIndexManagement),
        };
    }

    //Read
    public override async IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var filter = predicate != null
            ? Builders<TEntity>.Filter.Where(predicate)
            : Builders<TEntity>.Filter.Empty;

        await foreach (var item in GetAsync(filter, options, cancellationToken))
        {
            yield return item;
        }
    }

    public override async IAsyncEnumerable<TEntity> GetAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new Options<TEntity>();

        var skip = options.Skip ?? 0;
        var limit = options.Limit ?? ResultLimit ?? 1000;

        var page = 0;
        while (true)
        {
            var pageOptions = options with { Skip = skip, Limit = limit };
            var response = await GetManyAsync(filter, pageOptions, cancellationToken);

            foreach (var item in response.Items)
            {
                yield return item;
            }

            skip += response.Items.Length;

            if (skip >= response.TotalCount || response.Items.Length == 0)
            {
                break;
            }

            page++;
        }

        if (page >= 5)
        {
            _logger?.LogWarning($"Query on collection {{collection}} returned {{pages}} pages with items {{count}} each. Consired using {nameof(GetManyAsync)}, limit the total response or increase the count for each page.", CollectionName, page, limit);
        }
    }

    public override IAsyncEnumerable<T> GetProjectionAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, CancellationToken cancellationToken = default)
    {
        var filter = predicate != null
            ? Builders<T>.Filter.Where(predicate)
            : FilterDefinition<T>.Empty;

        return GetProjectionAsync(filter, options, cancellationToken);
    }

    public override async IAsyncEnumerable<T> GetProjectionAsync<T>(FilterDefinition<T> filter, Options<T> options = null, CancellationToken cancellationToken = default)
    {
        options ??= new Options<T>();

        var skip = options.Skip ?? 0;
        var limit = options.Limit ?? ResultLimit ?? 1000;

        var page = 0;
        while (true)
        {
            var pageOptions = options with { Skip = skip, Limit = limit };
            var response = await GetManyProjectionAsync<T>(filter, pageOptions, cancellationToken);

            foreach (var item in response.Items)
            {
                yield return item;
            }

            skip += response.Items.Length;

            if (skip >= response.TotalCount || response.Items.Length == 0)
            {
                break;
            }

            page++;
        }

        if (page >= 5)
        {
            _logger?.LogWarning($"Query on collection {{collection}} returned {{pages}} pages with items {{count}} each. Consired using {nameof(GetManyProjectionAsync)}, limit the total response or increase the count for each page.", CollectionName, page, limit);
        }
    }

    [Obsolete($"Use {nameof(GetManyAsync)} instead. This method will be deprecated.")]
    public virtual Task<Result<TEntity, TKey>> QueryAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return GetManyAsync(predicate, options, cancellationToken);
    }

    public override Task<Result<TEntity, TKey>> GetManyAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        var filter = predicate != null
            ? Builders<TEntity>.Filter.Where(predicate)
            : FilterDefinition<TEntity>.Empty;

        return GetManyAsync(filter, options, cancellationToken);
    }

    [Obsolete($"Use {nameof(GetManyAsync)} instead. This method will be deprecated.")]
    public virtual Task<Result<TEntity, TKey>> QueryAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return GetManyAsync(filter, options, cancellationToken);
    }

    public override async Task<Result<TEntity, TKey>> GetManyAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        filter ??= new FilterDefinitionBuilder<TEntity>().Empty;

        return await ExecuteAsync(nameof(GetManyAsync), async (collection, ct) =>
        {
            var o = BuildOptions(options);
            var cursor = await FindAsync(collection, filter, ct, o);
            var items = await BuildList(collection, cursor, ct).ToArrayAsync(ct);

            var totalCount = items.Length;
            if (totalCount <= (options?.Limit ?? ResultLimit ?? 1000))
            {
                totalCount = (int)await collection.CountDocumentsAsync(filter, cancellationToken: ct);
            }

            return (new Result<TEntity, TKey>
            {
                Items = items,
                TotalCount = totalCount
            }, items.Length);
        }, Operation.Read, cancellationToken);
    }

    public override Task<Result<T>> GetManyProjectionAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, CancellationToken cancellationToken = default)
    {
        var filter = predicate != null
            ? Builders<T>.Filter.Where(predicate)
            : Builders<T>.Filter.Empty;

        return GetManyProjectionAsync(filter, options, cancellationToken);
    }

    public override async Task<Result<T>> GetManyProjectionAsync<T>(FilterDefinition<T> filter, Options<T> options = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<Result<T>>(nameof(GetManyProjectionAsync), async (_, ct) =>
        {
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

            var items = new List<T>();

            var collection = await GetProjectionCollectionAsync<T>();
            var cursor = await collection.FindAsync(filter ?? FilterDefinition<T>.Empty, o, ct);
            var count = 0;
            while (await cursor.MoveNextAsync(ct))
            {
                foreach (var current in cursor.Current)
                {
                    count++;
                    if (ResultLimit != null && count > ResultLimit)
                    {
                        throw new ResultLimitException(ResultLimit.Value);
                    }

                    items.Add(current);
                }
            }

            var totalCount = items.Count;
            if (totalCount <= (options?.Limit ?? ResultLimit ?? 1000))
            {
                totalCount = (int)await collection.CountDocumentsAsync(filter, cancellationToken: ct);
            }

            return (new Result<T> { Items = items.ToArray(), TotalCount = totalCount }, items.Count);
        }, Operation.Read, cancellationToken);
    }

    public override async Task<TEntity> GetOneAsync(TKey id, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(nameof(GetOneAsync), async (collection, ct) =>
        {
            var filter = Builders<TEntity>.Filter.Eq(x => x.Id, id);
            var item = await collection.Find(filter).Limit(1).SingleOrDefaultAsync(ct);
            return (await CleanEntityAsync(collection, item), item == null ? 0 : 1);
        }, Operation.Read, cancellationToken);
    }

    public override Task<TEntity> GetOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        var filter = predicate == null ? FilterDefinition<TEntity>.Empty : new ExpressionFilterDefinition<TEntity>(predicate);
        return GetOneAsync(filter, options, cancellationToken);
    }

    public override async Task<TEntity> GetOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(nameof(GetOneAsync), async (collection, ct) =>
        {
            var sort = options?.Sort;
            var findFluent = collection.Find(filter).Sort(sort).Limit(2);
            TEntity item;
            switch (options?.Mode)
            {
                case null:
                case EMode.SingleOrDefault:
                    item = await findFluent.SingleOrDefaultAsync(cancellationToken: ct);
                    break;
                case EMode.Single:
                    item = await findFluent.SingleAsync(cancellationToken: ct);
                    break;
                case EMode.FirstOrDefault:
                    item = await findFluent.FirstOrDefaultAsync(cancellationToken: ct);
                    break;
                case EMode.First:
                    item = await findFluent.FirstAsync(cancellationToken: ct);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return (await CleanEntityAsync(collection, item), item == null ? 0 : 1);
        }, Operation.Read, cancellationToken);
    }

    public override Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var filter = predicate == null ? FilterDefinition<TEntity>.Empty : new ExpressionFilterDefinition<TEntity>(predicate);
        return CountAsync(filter, cancellationToken);
    }

    public override async Task<long> CountAsync(FilterDefinition<TEntity> filter, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(nameof(CountAsync), async (collection, ct) =>
        {
            var count = await collection.CountDocumentsAsync(filter, cancellationToken: ct);
            return (count, (int)count);
        }, Operation.Read, cancellationToken);
    }

    public override async Task<long> GetSizeAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(nameof(GetSizeAsync), (_, _) => Task.FromResult((_mongoDbService.GetSize(ProtectedCollectionName), 1)),Operation.Read, cancellationToken);
    }

    //Create
    public override async Task AddAsync(TEntity entity)
    {
        await ExecuteAsync(nameof(AddAsync), async (collection, ct) =>
        {
            await collection.InsertOneAsync(entity, cancellationToken: ct);
            return (true, 1);
        }, Operation.Create);
    }

    public override async Task<bool> TryAddAsync(TEntity entity)
    {
        return await ExecuteAsync(nameof(AddAsync), async (collection, ct) =>
        {
            try
            {
                await collection.InsertOneAsync(entity, cancellationToken: ct);
                return (true, 1);
            }
            catch (MongoWriteException)
            {
                return (false, 0);
            }

        }, Operation.Create);
    }

    public virtual async Task AddManyAsync(IEnumerable<TEntity> entities)
    {
        await ExecuteAsync(nameof(AddManyAsync), async (collection, ct) =>
        {
            var arr = entities.ToArray();
            await collection.InsertManyAsync(arr, cancellationToken: ct);
            return (true, arr.Length);
        }, Operation.Create);
    }

    //Update
    public virtual async Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity)
    {
        return await ExecuteAsync(nameof(AddOrReplaceAsync), async (collection, ct) =>
        {
            var filter = Builders<TEntity>.Filter.Eq(x => x.Id, entity.Id);
            var current = await collection.FindOneAndReplaceAsync(filter, entity, cancellationToken: ct);
            TEntity before = null;
            if (current == null)
            {
                await collection.InsertOneAsync(entity, cancellationToken: ct);
            }
            else
            {
                before = current;
            }

            return (new EntityChangeResult<TEntity>(before, entity), 1);
        }, Operation.Update);
    }

    public virtual Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, OneOption<TEntity> options = null)
    {
        var filter = Builders<TEntity>.Filter.Eq(x => x.Id, entity.Id);
        return ReplaceOneWithCheckAsync(entity, filter, options);
    }

    public virtual Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, FilterDefinition<TEntity> filter, OneOption<TEntity> options = null)
    {
        return ReplaceOneWithCheckAsync(entity, filter, options);
    }

    private async Task<EntityChangeResult<TEntity>> ReplaceOneWithCheckAsync(TEntity entity, FilterDefinition<TEntity> filter, OneOption<TEntity> options)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));

        return await ExecuteAsync(nameof(ReplaceOneWithCheckAsync), async (collection, ct) =>
        {
            var sort = options?.Sort;
            var findFluent = collection.Find(filter).Sort(sort).Limit(2);
            TEntity item;
            switch (options?.Mode)
            {
                case null:
                case EMode.SingleOrDefault:
                    item = await findFluent.SingleOrDefaultAsync(ct);
                    break;
                case EMode.Single:
                    item = await findFluent.SingleAsync(ct);
                    break;
                case EMode.FirstOrDefault:
                    {
                        var opt = new FindOneAndReplaceOptions<TEntity, TEntity> { Sort = sort };
                        var beforeUpdate = await collection.FindOneAndReplaceAsync(filter, entity, opt, cancellationToken: ct);
                        return (new EntityChangeResult<TEntity>(beforeUpdate, entity), 1);
                    }
                case EMode.First:
                    item = await findFluent.FirstAsync(ct);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (item == null) return (new EntityChangeResult<TEntity>(null, (TEntity)null), 0);

            filter = Builders<TEntity>.Filter.Eq(x => x.Id, item.Id);
            var before = await collection.FindOneAndReplaceAsync(filter, entity, cancellationToken: ct);
            return (new EntityChangeResult<TEntity>(before, entity), 1);

        }, Operation.Update);
    }

    public virtual Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update)
    {
        var filter = Builders<TEntity>.Filter.Eq(x => x.Id, id);
        return UpdateOneAsync(filter, update);
    }

    public virtual async Task<EntityChangeResult<TEntity>> UpdateOneAsync(Expression<Func<TEntity, bool>> predicate, UpdateDefinition<TEntity> update, OneOption<TEntity> options = null)
    {
        var filter = Builders<TEntity>.Filter.Where(predicate);
        return await UpdateOneAsync(filter, update);
    }

    public virtual async Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, OneOption<TEntity> options = null)
    {
        if (filter == null) throw new ArgumentException(nameof(filter));
        if (update == null) throw new ArgumentException(nameof(update));

        var response = await ExecuteAsync(nameof(UpdateOneAsync), async (collection, ct) =>
        {
            var sort = options?.Sort;
            var findFluent = collection.Find(filter).Sort(sort).Limit(2);
            TEntity item;
            switch (options?.Mode)
            {
                case null:
                case EMode.SingleOrDefault:
                    item = await findFluent.SingleOrDefaultAsync(ct);
                    break;
                case EMode.Single:
                    item = await findFluent.SingleAsync(ct);
                    break;
                case EMode.FirstOrDefault:
                    {
                        var opt = new FindOneAndUpdateOptions<TEntity, TEntity>
                        {
                            Sort = options.Sort,
                            ReturnDocument = ReturnDocument.Before,
                            IsUpsert = false
                        };
                        item = await collection.FindOneAndUpdateAsync(filter, update, opt, ct);
                        return (new EntityChangeResult<TEntity>(item, async () => { return await collection.Find(x => x.Id.Equals(item.Id)).SingleAsync(ct); }), 1);
                    }
                case EMode.First:
                    item = await findFluent.FirstAsync(ct);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (item == null) return (new EntityChangeResult<TEntity>(null, default(TEntity)), 0);

            var itemFilter = new FilterDefinitionBuilder<TEntity>().Eq(x => x.Id, item.Id);
            item = await collection.FindOneAndUpdateAsync(itemFilter, update, cancellationToken: ct);

            return (new EntityChangeResult<TEntity>(item, async () => { return await collection.Find(x => x.Id.Equals(item.Id)).SingleAsync(ct); }), 1);
        }, Operation.Update);

        return response;
    }

    public virtual async Task<long> UpdateAsync(Expression<Func<TEntity, bool>> predicate, UpdateDefinition<TEntity> update)
    {
        return await ExecuteAsync(nameof(UpdateAsync), async (collection, ct) =>
        {
            var result = await collection.UpdateManyAsync(predicate, update, cancellationToken: ct);
            return (result.ModifiedCount, (int)result.ModifiedCount);
        }, Operation.Update);
    }

    public override async Task<long> UpdateAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update)
    {
        return await ExecuteAsync(nameof(UpdateAsync), async (collection, ct) =>
        {
            var result = await collection.UpdateManyAsync(filter, update, cancellationToken: ct);
            return (result.ModifiedCount, (int)result.ModifiedCount);
        }, Operation.Update);
    }

    //Delete
    public override async Task<TEntity> DeleteOneAsync(TKey id)
    {
        return await DeleteOneAsync(x => x.Id.Equals(id));
    }

    public override async Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate, OneOption<TEntity> options = null)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        if (predicate == null) throw new ArgumentException(nameof(predicate));

        return await ExecuteAsync(nameof(UpdateOneAsync), async (collection, ct) =>
        {
            var sort = options?.Sort;
            var findFluent = collection.Find(predicate).Sort(sort).Limit(2);
            TEntity item;
            switch (options?.Mode)
            {
                case null:
                case EMode.SingleOrDefault:
                    item = await findFluent.SingleOrDefaultAsync(ct);
                    break;
                case EMode.Single:
                    item = await findFluent.SingleAsync(ct);
                    break;
                case EMode.FirstOrDefault:
                    {
                        var opt = new FindOneAndDeleteOptions<TEntity, TEntity> { Sort = options.Sort };
                        var deletedItem = await collection.FindOneAndDeleteAsync(predicate, opt, ct);
                        return (deletedItem, 1);
                    }
                case EMode.First:
                    item = await findFluent.FirstAsync(ct);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (item == null) return (null, 0);

            var itemFilter = new FilterDefinitionBuilder<TEntity>().Eq(x => x.Id, item.Id);
            await collection.FindOneAndDeleteAsync(itemFilter, cancellationToken: ct);
            return (item, 1);
        }, Operation.Delete);
    }

    public override async Task<TEntity> DeleteOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null)
    {
        throw new NotImplementedException();
    }

    public override async Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate)
    {
        return await ExecuteAsync(nameof(DeleteManyAsync), async (collection, ct) =>
        {
            var item = await collection.DeleteManyAsync(predicate, cancellationToken: ct);
            return (item.DeletedCount, (int)item.DeletedCount);
        }, Operation.Delete);
    }

    public override async Task<long> DeleteManyAsync(FilterDefinition<TEntity> filter)
    {
        return await ExecuteAsync(nameof(DeleteManyAsync), async (collection, ct) =>
        {
            var item = await collection.DeleteManyAsync(filter, cancellationToken: ct);
            return (item.DeletedCount, (int)item.DeletedCount);
        }, Operation.Delete);
    }

    //Other
    [Obsolete($"Use {nameof(GetCollectionScope)} instead. This method will be deprecated.")]
    public virtual IMongoCollection<TEntity> GetCollection()
    {
        var item = FetchCollectionAsync().GetAwaiter().GetResult();
        return item.Value;
    }

    public override Task<CollectionScope<TEntity>> GetCollectionScope(Operation operation)
    {
        var callKey = Guid.NewGuid();
        //var functionName = nameof(GetCollectionScope);

        //    await BeforeExecute(functionName, operation, callKey);

        //    return new CollectionScope<TEntity>(Collection, (elapsed, exception) =>
        //    {
        //        _logger?.Log(_executeInfoLogLevel, $"Executed {{repositoryType}} for {{CollectionName}} took {{elapsed}} ms. [action: Database, operation: {functionName}]", "DiskRepository", CollectionName, elapsed.TotalMilliseconds);
        //        InvokeAction(new ActionEventArgs.ActionData { Operation = functionName, Elapsed = elapsed });

        //        ((MongoDbServiceFactory)_mongoDbServiceFactory).OnCallEnd(this, new CallEndEventArgs(callKey, elapsed, exception, 1));
        //    });
        throw new NotImplementedException();
    }

    public override async Task DropCollectionAsync()
    {
        //TODO: Tell monitor that the collection has been dropped
        //await ExecuteAsync(nameof(DropCollectionAsync), async (collection, ct) =>
        //{
            await _mongoDbService.DropCollectionAsync(ProtectedCollectionName);
        //    return (true, 1);
        //}, Operation.Delete);
    }

    public override async IAsyncEnumerable<TEntity> GetDirtyAsync()
    {
        await foreach (var item in GetAsync())
        {
            if (item.NeedsCleaning()) yield return item;
        }
    }

    public override IEnumerable<(IndexFailOperation Operation, string Name)> GetFailedIndices()
    {
        return _initiationLibrary.GetFailedIndices(ServerName, DatabaseName, ProtectedCollectionName);
    }

    private async Task<IMongoCollection<T>> GetProjectionCollectionAsync<T>()
    {
        throw new NotImplementedException();
    }

    internal override async Task<StepResponse<IMongoCollection<TEntity>>> FetchCollectionAsync(bool initiate = true)
    {
        var fullName = $"{ConfigurationName ?? Constants.DefaultConfigurationName}.{DatabaseName}.{CollectionName}";

        if (_collectionPool.TryGetCollection<TEntity>(fullName, out var collection))
        {
            return new StepResponse<IMongoCollection<TEntity>>
            {
                Timestamp = Stopwatch.GetTimestamp(),
                Value = collection,
                Step = nameof(FetchCollectionAsync),
            };
        }

        await _fetchLock.WaitAsync();
        try
        {
            if (_collectionPool.TryGetCollection(fullName, out collection))
            {
                return new StepResponse<IMongoCollection<TEntity>>
                {
                    Timestamp = Stopwatch.GetTimestamp(),
                    Value = collection,
                    Step = nameof(FetchCollectionAsync),
                    Message = "Waited for another task to initate.",
                };
            }

            collection = await _mongoDbService.GetCollectionAsync<TEntity>(ProtectedCollectionName);

            string message = null;
            if (initiate && _initiationLibrary.ShouldInitiate(ServerName, DatabaseName, ProtectedCollectionName))
            {
                //_logger?.LogTrace($"Starting to initiate {{collection}}. [action: Database, operation: {nameof(FetchCollectionAsync)}]", ProtectedCollectionName);
                //InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(FetchCollectionAsync), Message = "Starting to initiate.", Level = LogLevel.Trace });
                RegisterTypes();

                var exists = await _mongoDbService.DoesCollectionExist(ProtectedCollectionName);
                if (exists)
                {
                    await AssureIndex(collection);
                    await CleanAsync(collection);
                    await DropEmptyAsync(collection);
                }
                else if (CreateCollectionStrategy == CreateStrategy.CreateOnGet)
                {
                    collection = await _mongoDbService.CreateCollectionAsync<TEntity>(ProtectedCollectionName);
                    await AssureIndex(collection);
                }

                await InitAsync(collection);
                //_logger?.LogTrace($"Initiate {{collection}} is completed. [action: Database, operation: {nameof(FetchCollectionAsync)}]", ProtectedCollectionName);
                //InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(FetchCollectionAsync), Message = "Initiation completed.", Level = LogLevel.Trace });

                //initiateAction = InitiateAction.InitiationPerformed;
                message = "Initiated collection.";
            }
            else
            {
                //_logger?.LogTrace($"Skip initiation of {{collection}} because it has already been initiated. [action: Database, operation: {nameof(FetchCollectionAsync)}]", ProtectedCollectionName);
                //InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(FetchCollectionAsync), Message = "Skip initiation because it has already been completed.", Level = LogLevel.Trace });
                //initiateAction = InitiateAction.NoAction;
            }

            _collectionPool.AddCollection(fullName, collection);
            return new StepResponse<IMongoCollection<TEntity>>
            {
                Timestamp = Stopwatch.GetTimestamp(),
                Value = collection,
                Step = nameof(FetchCollectionAsync),
                Message = message
            };
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    internal override async Task<bool> AssureIndex(IMongoCollection<TEntity> collection, bool forceAssure = false, bool throwOnException = false)
    {
        var assureIndexMode = _mongoDbService.GetAssureIndexMode();
        if (assureIndexMode == AssureIndexMode.Disabled && !forceAssure)
        {
            _logger?.LogTrace("Assure index is disabled.");
            return false;
        }

        if (forceAssure || _initiationLibrary.ShouldInitiateIndex(ServerName, DatabaseName, ProtectedCollectionName))
        {
            //_logger?.LogTrace($"Assure index for collection {{collection}} in {{repositoryType}}. [action: Database, operation: {nameof(CleanAsync)}]", ProtectedCollectionName, "DiskRepository");

            //Not sure why this index should be created like this. Trying to disable.
            //await collection.Indexes.CreateOneAsync(new CreateIndexModel<TEntity>(Builders<TEntity>.IndexKeys.Ascending(x => x.Id).Ascending("_t"), new CreateIndexOptions()));

            await UpdateIndicesAsync(collection, assureIndexMode, throwOnException);
            return true;
        }

        return false;
    }

    internal override async Task<(int Before, int After)> DropIndex(IMongoCollection<TEntity> collection)
    {
        var before = (await collection.Indexes.ListAsync())
            .ToList()
            .Select(x => x.GetValue("name").AsString)
            .Count(x => !x.StartsWith("_id_"));

        await collection.Indexes.DropAllAsync();

        var fingerprint = new CollectionFingerprint
        {
            ConfigurationName = ConfigurationName ?? _mongoDbService.GetConfigurationName(),
            CollectionName = ProtectedCollectionName,
            DatabaseName = DatabaseName
        };
        ((MongoDbServiceFactory)_mongoDbServiceFactory).OnIndexUpdatedEvent(this, new IndexUpdatedEventArgs(fingerprint));

        var after = (await collection.Indexes.ListAsync())
            .ToList()
            .Select(x => x.GetValue("name").AsString)
            .Count(x => !x.StartsWith("_id_"));

        return (before, after);
    }

    internal override async Task CleanAsync(IMongoCollection<TEntity> collection)
    {
        if (!CleanOnStartup) return;

        //TODO: Implement Clean
        //if (!AutoClean)
        //{
        //    _logger?.LogWarning($"Both CleanOnStartup and AutoClean for collection {{collectionName}} has to be true for cleaning to run on startup. [action: Database, operation: {nameof(CleanAsync)}]", ProtectedCollectionName);
        //    InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(CleanAsync), Message = "Both CleanOnStartup and AutoClean for has to be true for cleaning to run on startup.", Level = LogLevel.Warning });
        //    return;
        //}

        //var sw = new Stopwatch();
        //sw.Start();

        //var filter = Builders<TEntity>.Filter.Empty;

        //try
        //{
        //    _logger?.LogTrace($"Starting to clean in collection {{collection}} in {{repositoryType}}. [action: Database, operation: {nameof(CleanAsync)}]", ProtectedCollectionName, "DiskRepository");

        //    using var cursor = await FindAsync(collection, filter, CancellationToken.None, null);
        //    var allItems = await cursor.ToListAsync();
        //    var items = allItems.Where(x => x.NeedsCleaning());
        //    var totalCount = allItems.Count;
        //    var count = 0;
        //    foreach (var item in items)
        //    {
        //        count++;
        //        await CleanEntityAsync(collection, item);
        //    }

        //    sw.Stop();
        //    if (count == 0)
        //    {
        //        _logger?.LogTrace($"Nothing to clean in collection {{collection}} in {{repositoryType}}. [action: Database, operation: {nameof(CleanAsync)}]", ProtectedCollectionName, "DiskRepository");
        //        InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(CleanAsync), Message = "Nothing to clean.", Level = LogLevel.Trace });
        //    }
        //    else
        //    {
        //        _logger?.LogInformation($"Cleaned {{count}} of {{totalCount}} took {{elapsed}} ms in collection {{collection}} in {{repositoryType}}. [action: Database, operation: {nameof(CleanAsync)}]", count, totalCount, sw.Elapsed.TotalMilliseconds, ProtectedCollectionName, "DiskRepository");
        //        InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(CleanAsync), Message = "Cleaned completed.", ItemCount = count, Elapsed = sw.Elapsed });
        //    }
        //}
        //catch (FormatException)
        //{
        //    _logger?.LogError("Failed to clean collection {collection} in {repositoryType}.", ProtectedCollectionName, "DiskRepository");
        //}

        Debugger.Break();
        throw new NotImplementedException($"{nameof(CleanAsync)} has not been implemented.");
    }

    protected virtual async Task DropEmptyAsync(IMongoCollection<TEntity> collection)
    {
        if (CreateCollectionStrategy != CreateStrategy.DropEmpty) return;

        var any = (await collection.CountDocumentsAsync(x => true, new CountOptions { Limit = 1 })) != 0;

        if (any) return;

        await DropCollectionAsync();
    }

    private bool ArmRecheckInvalidIndex()
    {
        return _initiationLibrary.RecheckInitiateIndex(ServerName, DatabaseName, ProtectedCollectionName);
    }

    private async Task UpdateIndicesAsync(IMongoCollection<TEntity> collection, AssureIndexMode assureIndexMode, bool throwOnException)
    {
        switch (assureIndexMode)
        {
            case AssureIndexMode.ByName:
                await UpdateIndicesByNameAsync(collection, throwOnException);
                break;
            case AssureIndexMode.BySchema:
                await UpdateIndicesBySchemaAsync(collection, throwOnException);
                break;
            case AssureIndexMode.DropCreate:
                await UpdateIndicesByDropCreateAsync(collection, throwOnException);
                break;
            case AssureIndexMode.Disabled:
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(assureIndexMode), assureIndexMode, null);
        }
    }

    #region Update Indices

    private async Task UpdateIndicesByNameAsync(IMongoCollection<TEntity> collection, bool throwOnException)
    {
        var indices = (CoreIndices?.ToArray() ?? []).Union(Indices?.ToArray() ?? []).ToArray();

        var firstInvalid = indices.GroupBy(x => x.Options.Name).FirstOrDefault(x => x.Count() > 1);
        if (firstInvalid != null) throw new InvalidOperationException($"Indices can only be defined once with the same name. Index {firstInvalid.First().Options.Name} has been defined {firstInvalid.Count()} times for collection {ProtectedCollectionName}.");

        if (indices.Any(x => string.IsNullOrEmpty(x.Options.Name))) throw new InvalidOperationException("Indices needs to have a name.");

        var allExistingIndexNames = (await collection.Indexes.ListAsync()).ToList()
            .Select(x => x.GetValue("name").AsString)
            .ToArray();
        var existingIndexNames = allExistingIndexNames
            .Where(x => !x.StartsWith("_id_"))
            .ToArray();

        _logger?.Log(_executeInfoLogLevel, "Assure index for collection {collection} with {count} documents.", ProtectedCollectionName, await collection.CountDocumentsAsync(x => true));
        _logger?.LogTrace("All existing indices in collection {collection}: {indices}.", ProtectedCollectionName, string.Join(", ", allExistingIndexNames));
        _logger?.LogDebug("Existing, non system, indices in collection {collection}: {indices}.", ProtectedCollectionName, string.Join(", ", existingIndexNames));
        _logger?.LogDebug("Defined indices for collection {collection}: {indices}.", ProtectedCollectionName, string.Join(", ", indices.Select(x => x.Options.Name)));

        var hasChanged = false;

        //NOTE: Drop indexes not in list
        foreach (var indexName in existingIndexNames)
        {
            if (indices.All(x => x.Options.Name != indexName))
            {
                try
                {
                    _logger?.LogDebug("Index {indexName} will be dropped in collection {collection}.", indexName, ProtectedCollectionName);
                    await collection.Indexes.DropOneAsync(indexName);
                    _logger?.LogInformation("Index {indexName} was dropped in collection {collection}.", indexName, ProtectedCollectionName);
                    hasChanged = true;
                }
                catch (Exception e)
                {
                    Debugger.Break();
                    _logger?.LogError(e, "Failed to drop index {indexName} in collection {collection}. {message}", indexName, ProtectedCollectionName, e.Message);
                    _initiationLibrary.AddFailedInitiateIndex(ServerName, DatabaseName, ProtectedCollectionName, (IndexFailOperation.Drop, indexName));
                    if (throwOnException) throw;
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
                    _logger?.LogDebug("Index {indexName} will be created in collection {collection}.", index.Options.Name, ProtectedCollectionName);
                    var message = await collection.Indexes.CreateOneAsync(index);
                    _logger?.LogInformation("Index {indexName} was created in collection {collection}. {message}", index.Options.Name, ProtectedCollectionName, message);
                    hasChanged = true;
                }
                catch (Exception e)
                {
                    Debugger.Break();
                    _logger?.LogError(e, "Failed to create index {indexName} in collection {collection}. {message}", index.Options.Name, ProtectedCollectionName, e.Message);
                    _initiationLibrary.AddFailedInitiateIndex(ServerName, DatabaseName, ProtectedCollectionName, (IndexFailOperation.Create, index.Options.Name));
                    if (throwOnException) throw;
                }
            }
        }

        if (hasChanged)
        {
            var fingerprint = new CollectionFingerprint
            {
                ConfigurationName = ConfigurationName ?? _mongoDbService.GetConfigurationName(),
                CollectionName = ProtectedCollectionName,
                DatabaseName = DatabaseName
            };
            ((MongoDbServiceFactory)_mongoDbServiceFactory).OnIndexUpdatedEvent(this, new IndexUpdatedEventArgs(fingerprint));
        }
    }

    private async Task UpdateIndicesBySchemaAsync(IMongoCollection<TEntity> collection, bool throwOnException)
    {
        var indices = (CoreIndices?.ToArray() ?? []).Union(Indices?.ToArray() ?? []).ToArray();

        var existingIndiceModel = (await MongoDbService.BuildIndicesModel(collection))
            .Where(x => !x.Name.StartsWith("_id_"))
            .ToArray();
        var definedIndiceModel = this.BuildIndexMetas().ToArray();

        var hasChanged = false;

        //NOTE: Drop indexes not in list
        foreach (var collectionIndexModel in existingIndiceModel)
        {
            if (definedIndiceModel.All(x => x != collectionIndexModel))
            {
                var indexName = collectionIndexModel.Name;

                try
                {
                    _logger?.LogDebug("Index {indexName} will be dropped in collection {collection}.", indexName, ProtectedCollectionName);
                    await collection.Indexes.DropOneAsync(indexName);
                    _logger?.LogInformation("Index {indexName} was dropped in collection {collection}.", indexName, ProtectedCollectionName);
                    hasChanged = true;
                }
                catch (Exception e)
                {
                    Debugger.Break();
                    _logger?.LogError(e, "Failed to drop index {indexName} in collection {collection}. {message}", indexName, ProtectedCollectionName, e.Message);
                    _initiationLibrary.AddFailedInitiateIndex(ServerName, DatabaseName, ProtectedCollectionName, (IndexFailOperation.Drop, indexName));
                    if (throwOnException) throw;
                }
            }
        }

        //NOTE: Create indexes in the list
        foreach (var definedIndexModel in definedIndiceModel)
        {
            if (existingIndiceModel.All(x => x != definedIndexModel))
            {
                try
                {
                    var index = indices.Single(x => x.Options.Name == definedIndexModel.Name);

                    _logger?.LogDebug("Index {indexName} will be created in collection {collection}.", definedIndexModel.Name, ProtectedCollectionName);
                    var message = await collection.Indexes.CreateOneAsync(index);
                    _logger?.LogInformation("Index {indexName} was created in collection {collection}. {message}", definedIndexModel.Name, ProtectedCollectionName, message);
                    hasChanged = true;
                }
                catch (Exception e)
                {
                    Debugger.Break();
                    _logger?.LogError(e, "Failed to create index {indexName} in collection {collection}. {message}", definedIndexModel.Name, ProtectedCollectionName, e.Message);
                    _initiationLibrary.AddFailedInitiateIndex(ServerName, DatabaseName, ProtectedCollectionName, (IndexFailOperation.Create, definedIndexModel.Name));
                    if (throwOnException) throw;
                }
            }
        }

        if (hasChanged)
        {
            var fingerprint = new CollectionFingerprint
            {
                ConfigurationName = ConfigurationName ?? _mongoDbService.GetConfigurationName(),
                CollectionName = ProtectedCollectionName,
                DatabaseName = DatabaseName
            };
            ((MongoDbServiceFactory)_mongoDbServiceFactory).OnIndexUpdatedEvent(this, new IndexUpdatedEventArgs(fingerprint));
        }
    }

    private async Task UpdateIndicesByDropCreateAsync(IMongoCollection<TEntity> collection, bool throwOnException)
    {
        var indices = (CoreIndices?.ToArray() ?? []).Union(Indices?.ToArray() ?? []).ToArray();

        var allExistingIndexNames = (await collection.Indexes.ListAsync()).ToList()
            .Select(x => x.GetValue("name").AsString)
            .ToArray();
        var existingIndexNames = allExistingIndexNames
            .Where(x => !x.StartsWith("_id_"))
            .ToArray();

        //NOTE: Drop indexes not in list
        foreach (var indexName in existingIndexNames)
        {
            try
            {
                _logger?.LogDebug("Index {indexName} will be dropped in collection {collection}.", indexName, ProtectedCollectionName);
                await collection.Indexes.DropOneAsync(indexName);
                _logger?.LogInformation("Index {indexName} was dropped in collection {collection}.", indexName, ProtectedCollectionName);
            }
            catch (Exception e)
            {
                Debugger.Break();
                _logger?.LogError(e, "Failed to drop index {indexName} in collection {collection}. {message}", indexName, ProtectedCollectionName, e.Message);
                _initiationLibrary.AddFailedInitiateIndex(ServerName, DatabaseName, ProtectedCollectionName, (IndexFailOperation.Drop, indexName));
                if (throwOnException) throw;
            }
        }

        //NOTE: Create indexes in the list
        foreach (var index in indices)
        {
            try
            {
                _logger?.LogDebug("Index {indexName} will be created in collection {collection}.", index.Options.Name, ProtectedCollectionName);
                var message = await collection.Indexes.CreateOneAsync(index);
                _logger?.LogInformation("Index {indexName} was created in collection {collection}. {message}", index.Options.Name, ProtectedCollectionName, message);
            }
            catch (Exception e)
            {
                Debugger.Break();
                _logger?.LogError(e, "Failed to create index {indexName} in collection {collection}. {message}", index.Options.Name, ProtectedCollectionName, e.Message);
                _initiationLibrary.AddFailedInitiateIndex(ServerName, DatabaseName, ProtectedCollectionName, (IndexFailOperation.Create, index.Options.Name));
                if (throwOnException) throw;
            }
        }

        var fingerprint = new CollectionFingerprint
        {
            ConfigurationName = ConfigurationName ?? _mongoDbService.GetConfigurationName(),
            CollectionName = ProtectedCollectionName,
            DatabaseName = DatabaseName
        };
        ((MongoDbServiceFactory)_mongoDbServiceFactory).OnIndexUpdatedEvent(this, new IndexUpdatedEventArgs(fingerprint));
    }

    #endregion

    private async IAsyncEnumerable<TEntity> BuildList(IMongoCollection<TEntity> collection, IAsyncCursor<TEntity> cursor, [EnumeratorCancellation] CancellationToken cancellationToken)
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

                yield return await CleanEntityAsync(collection, current);
            }
        }
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
                InvokeAction(new ActionEventArgs.ActionData { Operation = nameof(CleanEntityAsync), Message = "Entity needs cleaning.", Level = Microsoft.Extensions.Logging.LogLevel.Warning, Data = new Dictionary<string, object> { { "id", item.Id } } });
            }
        }

        return item;
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

    private static FindOptions<TEntity, TEntity> BuildOptions(Options<TEntity> options)
    {
        FindOptions<TEntity, TEntity> o = null;
        if (options != null)
        {
            o = new FindOptions<TEntity, TEntity>
            {
                Sort = options.Sort,
                Limit = options.Limit,
                Skip = options.Skip
            };

            if (options.Projection != null)
            {
                o.Projection = options.Projection;
            }
        }

        return o;
    }

    private void RegisterTypes()
    {
        if ((typeof(TEntity).IsInterface || typeof(TEntity).IsAbstract) && (Types == null || !Types.Any()))
        {
            var kind = typeof(TEntity).IsInterface ? "an interface" : "an abstract class";
            throw new InvalidOperationException($"Types needs to be provided since '{typeof(TEntity).Name}' is {kind}. Do this by overriding the the Types property in '{GetType().Name}' and provide the requested type.");
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

    private static TimeSpan GetElapsed(long from, long to)
    {
        return TimeSpan.FromSeconds((to - from) / (double)Stopwatch.Frequency);
    }

    private static ProjectionDefinition<T> BuildProjection<T>()
    {
        var builder = new ProjectionDefinitionBuilder<T>();
        var props = typeof(T).GetProperties();
        var projections = props.Select(x => Builders<T>.Projection.Include(x.Name));
        var projectionDefinition = builder.Combine(projections);
        return projectionDefinition;
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
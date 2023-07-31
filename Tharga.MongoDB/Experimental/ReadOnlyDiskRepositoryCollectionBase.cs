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

namespace Tharga.MongoDB.Experimental;

public abstract class ReadOnlyDiskRepositoryCollectionBase<TEntity, TKey> : RepositoryCollectionBase<TEntity, TKey>, IReadOnlyDiskRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IMongoCollection<TEntity> _collection;

    protected ReadOnlyDiskRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger = null, DatabaseContext databaseContext = null)
        :base(mongoDbServiceFactory, logger, databaseContext)
    {
    }

    internal IMongoCollection<TEntity> Collection => _collection ??= Task.Run(async () => await FetchCollectionAsync()).Result;

    public override Task<long> GetSizeAsync()
    {
        throw new NotImplementedException();
    }

    public override async IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default)
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

    public override IAsyncEnumerable<T> GetAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    internal async Task<IAsyncCursor<TEntity>> FindAsync(IMongoCollection<TEntity> collection, FilterDefinition<TEntity> filter, CancellationToken cancellationToken, FindOptions<TEntity, TEntity> options)
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

    public async IAsyncEnumerable<TEntity> GetAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        var sw = new Stopwatch();
        sw.Start();

        var o = options == null ? null : new FindOptions<TEntity, TEntity> { Projection = options.Projection, Sort = options.Sort, Limit = options.Limit };
        var cursor = await FindAsync(Collection, filter, cancellationToken, o);

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

    public IAsyncEnumerable<ResultPage<TEntity, TKey>> GetPageAsync(Expression<Func<TEntity, bool>> predicate, Options<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<TEntity> GetOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<long> CountAsync(FilterDefinition<TEntity> filter)
    {
        throw new NotImplementedException();
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
            catch (Exception e)
            {
                _logger.LogError(e, e.Message);
                throw;
            }
            finally
            {
                _lock.Release();
            }
        }, false);
    }

    private void RegisterTypes()
    {
        if ((typeof(TEntity).IsInterface || typeof(TEntity).IsAbstract) && (Types == null || !Types.Any()))
        {
            //TODO: Can this be done automatically?
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

    internal override Task AssureIndex()
    {
        return AssureIndex(Collection);
    }

    internal virtual Task AssureIndex(IMongoCollection<TEntity> collection)
    {
        return Task.CompletedTask;
    }

    internal virtual Task CleanAsync(IMongoCollection<TEntity> collection)
    {
        return Task.CompletedTask;
    }

    internal virtual Task DropEmpty(IMongoCollection<TEntity> collection)
    {
        return Task.CompletedTask;
    }

    internal virtual Task<TEntity> CleanEntityAsync(TEntity item)
    {
        return Task.FromResult(item);
    }
}
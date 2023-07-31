using System;
using System.Collections;
using System.Collections.Concurrent;
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
using Tharga.MongoDB.Buffer;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Internals;
using static Tharga.MongoDB.ActionEventArgs;

namespace Tharga.MongoDB.Experimental;

public abstract class RepositoryCollectionBase
{
    public static event EventHandler<ActionEventArgs> ActionEvent;

    internal void InvokeAction(ActionEventArgs.ActionData actionData, ActionEventArgs.ContextData contextData)
    {
        ActionEvent?.Invoke(this, new ActionEventArgs(actionData, contextData));
    }
}

public abstract class RepositoryCollectionBase<TEntity, TKey> : RepositoryCollectionBase
    where TEntity : EntityBase<TKey>
{
    private readonly Lazy<ActionEventArgs.ContextData> _contextData;

    protected readonly ILogger<RepositoryCollectionBase<TEntity, TKey>> _logger;
    protected readonly DatabaseContext _databaseContext;
    protected readonly IMongoDbService _mongoDbService;

    protected RepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger, DatabaseContext databaseContext)
    {
        _logger = logger;
        _databaseContext = databaseContext;

        _mongoDbService = mongoDbServiceFactory.GetMongoDbService(() => _databaseContext ?? new DatabaseContext { CollectionName = CollectionName, DatabasePart = DatabasePart, ConfigurationName = ConfigurationName });
        _contextData = new Lazy<ActionEventArgs.ContextData>(BuildContextData);
    }

    //internal virtual IRepositoryCollection<TEntity, TKey> BaseCollection => this;
    private string DefaultCollectionName => typeof(TEntity).Name;
    protected string ProtectedCollectionName => CollectionName.ProtectCollectionName();

    internal virtual string ServerName => _mongoDbService.GetDatabaseHostName();
    internal virtual string DatabaseName => _mongoDbService.GetDatabaseName();
    public virtual string CollectionName => _databaseContext?.CollectionName ?? DefaultCollectionName;
    public virtual string DatabasePart => _databaseContext?.CollectionName;
    public virtual string ConfigurationName => _databaseContext?.ConfigurationName.Value;
    //public virtual bool AutoClean => _mongoDbService.GetAutoClean();
    //public virtual bool CleanOnStartup => _mongoDbService.GetCleanOnStartup();
    //public virtual bool DropEmptyCollections => _mongoDbService.DropEmptyCollections();
    public virtual int? ResultLimit => _mongoDbService.GetResultLimit();
    //public virtual IEnumerable<CreateIndexModel<TEntity>> Indicies => null;
    public virtual IEnumerable<Type> Types => null;

    protected virtual async Task<T> Execute<T>(string functionName, Func<Task<T>> action, bool assureIndex)
    {
        var sw = new Stopwatch();
        sw.Start();

        try
        {
            if (assureIndex)
            {
                //TODO: await AssureIndex(Collection);
            }

            var result = await action.Invoke();

            sw.Stop();

            //TODO: _logger?.LogInformation($"Executed {{repositoryType}} took {{elapsed}} ms. [action: Database, operation: {functionName}]", "DiskRepository", sw.Elapsed.TotalMilliseconds);
            //TODO: InvokeAction(new ActionEventArgs.ActionData { Operation = functionName, Elapsed = sw.Elapsed });

            return result;
        }
        catch (Exception e)
        {
            //TODO: _logger?.LogError(e, $"Exception {{repositoryType}}. [action: Database, operation: {functionName}]", "DiskRepository");
            //TODO: InvokeAction(new ActionEventArgs.ActionData { Operation = functionName, Exception = e });
            throw;
        }
    }

    internal void InvokeAction(ActionEventArgs.ActionData actionData)
    {
        InvokeAction(actionData, _contextData.Value);
    }
    public Task<long> GetSizeAsync()
    {
        throw new NotImplementedException();
    }

    public abstract IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    public abstract IAsyncEnumerable<T> GetAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, CancellationToken cancellationToken = default) where T : TEntity;
    public abstract Task<TEntity> GetOneAsync(TKey id, CancellationToken cancellationToken = default);
    public abstract Task<TEntity> GetOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null, CancellationToken cancellationToken = default);
    public abstract Task<T> GetOneAsync<T>(Expression<Func<T, bool>> predicate = null, OneOption<T> options = null, CancellationToken cancellationToken = default) where T : TEntity;
    public abstract Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate);

    private ActionEventArgs.ContextData BuildContextData()
    {
        return new ActionEventArgs.ContextData
        {
            CollectionName = CollectionName,
            CollectionType = GetType().Name,
            DatabaseName = DatabaseName,
            EntityType = typeof(TEntity).Name,
            ServerName = ServerName
        };
    }
}

public abstract class ReadOnlyBufferRepositoryCollectionBase<TEntity, TKey> : RepositoryCollectionBase<TEntity, TKey>, IReadOnlyBufferRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private readonly IBufferCollection<TEntity, TKey> _bufferCollection;
    private readonly SemaphoreSlim _bufferLoadLock = new(1, 1);
    private ReadOnlyDiskRepositoryCollectionBase<TEntity, TKey> _disk;
    private bool _diskConnected = true;

    protected ReadOnlyBufferRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<ReadOnlyBufferRepositoryCollectionBase<TEntity, TKey>> logger = null, DatabaseContext databaseContext = null)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _bufferCollection = BufferLibrary.GetBufferCollection<TEntity, TKey>();
    }

    //internal override IRepositoryCollection<TEntity, TKey> BaseCollection => _diskConnected ? Disk : this;
    internal ReadOnlyDiskRepositoryCollectionBase<TEntity, TKey> Disk => _diskConnected ? _disk ??= new GenericReadOnlyDiskRepositoryCollection<TEntity, TKey>(_mongoDbServiceFactory, _databaseContext ?? new DatabaseContext { CollectionName = CollectionName, DatabasePart = DatabasePart, ConfigurationName = ConfigurationName }, _logger, this) : null;

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

public abstract class ReadWriteBufferRepositoryCollectionBase<TEntity, TKey> : ReadOnlyBufferRepositoryCollectionBase<TEntity, TKey>, IBufferRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    protected ReadWriteBufferRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<ReadWriteBufferRepositoryCollectionBase<TEntity, TKey>> logger = null, DatabaseContext databaseContext = null)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }

    public Task DropCollectionAsync()
    {
        throw new NotImplementedException();
    }

    public Task<bool> AddAsync(TEntity entity)
    {
        throw new NotImplementedException();
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
}

public abstract class ReadOnlyDiskRepositoryCollectionBase<TEntity, TKey> : RepositoryCollectionBase<TEntity, TKey>, IReadOnlyDiskRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IMongoCollection<TEntity> _collection;

    protected ReadOnlyDiskRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger = null, DatabaseContext databaseContext = null)
        :base(mongoDbServiceFactory, logger, databaseContext)
    {
    }

    //TODO: Make sure this property is hidden from the derived classes
    protected IMongoCollection<TEntity> Collection => _collection ??= Task.Run(async () => await FetchCollectionAsync()).Result;

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

                yield return current; //TODO: Create an override for this in ther read-write-repo: await CleanEntityAsync(current);
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
                        //TODO: Move to read-write class. await AssureIndex(collection);
                        //TODO: Move to read-write class. await CleanAsync(collection);
                        //TODO: Move to read-write class. await DropEmpty(collection);
                    }

                    //TODO: Move to read-write class. await InitAsync(collection);
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

    //private async Task AssureIndex(IMongoCollection<TEntity> collection)
    //{
    //    if (InitiationLibrary.ShouldInitiateIndex(ServerName, DatabaseName, ProtectedCollectionName))
    //    {
    //        await collection.Indexes.CreateOneAsync(new CreateIndexModel<TEntity>(Builders<TEntity>.IndexKeys.Ascending(x => x.Id).Ascending("_t"), new CreateIndexOptions()));
    //        await UpdateIndiciesAsync(collection);
    //    }
    //}

    //private async Task UpdateIndiciesAsync(IMongoCollection<TEntity> collection)
    //{
    //    if (Indicies == null) return;

    //    if (Indicies.Any(x => string.IsNullOrEmpty(x.Options.Name))) throw new InvalidOperationException("Indicies needs to have a name.");

    //    //NOTE: Drop indexes not in list
    //    var indicies = (await collection.Indexes.ListAsync()).ToList();
    //    foreach (var index in indicies)
    //    {
    //        var indexName = index.GetValue("name").AsString;
    //        if (!indexName.StartsWith("_id_"))
    //        {
    //            if (Indicies.All(x => x.Options.Name != indexName))
    //            {
    //                await collection.Indexes.DropOneAsync(indexName);
    //            }
    //        }
    //    }

    //    //NOTE: Create indexes in the list
    //    foreach (var index in Indicies)
    //    {
    //        await collection.Indexes.CreateOneAsync(index);
    //    }
    //}
}

public abstract class ReadWriteDiskRepositoryCollectionBase<TEntity, TKey> : ReadOnlyDiskRepositoryCollectionBase<TEntity, TKey>, IDiskRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    protected ReadWriteDiskRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<ReadWriteDiskRepositoryCollectionBase<TEntity, TKey>> logger = null, DatabaseContext databaseContext = null)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }

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
}

internal class GenericReadOnlyDiskRepositoryCollection<TEntity, TKey> : ReadOnlyDiskRepositoryCollectionBase<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    private readonly ReadOnlyBufferRepositoryCollectionBase<TEntity, TKey> _buffer;

    public GenericReadOnlyDiskRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger, ReadOnlyBufferRepositoryCollectionBase<TEntity, TKey> buffer)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
        _buffer = buffer;
    }

    internal override string ServerName => _buffer?.ServerName ?? base.ServerName;
    internal override string DatabaseName => _buffer?.DatabaseName ?? base.DatabaseName;
    public override string CollectionName => _buffer?.CollectionName ?? base.CollectionName;
    public override string DatabasePart => _buffer?.DatabasePart ?? base.DatabasePart;
    public override string ConfigurationName => _buffer?.ConfigurationName ?? base.ConfigurationName;
    //public override bool AutoClean => _buffer?.AutoClean ?? base.AutoClean;
    //public override bool CleanOnStartup => _buffer?.CleanOnStartup ?? base.CleanOnStartup;
    //public override bool DropEmptyCollections => _buffer?.DropEmptyCollections ?? base.DropEmptyCollections;
    public override int? ResultLimit => _buffer?.ResultLimit ?? base.ResultLimit;
    //public override IEnumerable<CreateIndexModel<TEntity>> Indicies => _buffer?.Indicies ?? base.Indicies;
    public override IEnumerable<Type> Types => _buffer?.Types ?? base.Types;
}
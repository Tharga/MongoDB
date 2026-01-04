using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB;

public abstract class RepositoryCollectionBase
{
    public static event EventHandler<ActionEventArgs> ActionEvent;

    internal abstract string ServerName { get; }
    internal abstract string DatabaseName { get; }
    public abstract string CollectionName { get; }
    public abstract string ConfigurationName { get; }
    public abstract long? VirtualCount { get; }

    internal void InvokeAction(ActionEventArgs.ActionData actionData, ActionEventArgs.ContextData contextData)
    {
        ActionEvent?.Invoke(this, new ActionEventArgs(actionData, contextData));
    }
}

public abstract class RepositoryCollectionBase<TEntity, TKey> : RepositoryCollectionBase, IRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    protected readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    protected readonly ILogger<RepositoryCollectionBase<TEntity, TKey>> _logger;
    protected readonly DatabaseContext _databaseContext;
    protected readonly IMongoDbService _mongoDbService;
    protected readonly LogLevel _executeInfoLogLevel;

    private readonly Lazy<ActionEventArgs.ContextData> _contextData;
    internal readonly IExecuteLimiter _executeLimiter;

    protected RepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger, DatabaseContext databaseContext = null)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _logger = logger;
        _databaseContext = databaseContext;

        _mongoDbService = mongoDbServiceFactory.GetMongoDbService(() => _databaseContext ?? new DatabaseContext { CollectionName = CollectionName, DatabasePart = DatabasePart, ConfigurationName = ConfigurationName });
        _contextData = new Lazy<ActionEventArgs.ContextData>(BuildContextData);

        _executeInfoLogLevel = _mongoDbService.GetExecuteInfoLogLevel();
        _executeLimiter = ((MongoDbService)_mongoDbService).ExecuteLimiter;
    }

    internal virtual IRepositoryCollection<TEntity, TKey> BaseCollection => this;
    private string DefaultCollectionName => typeof(TEntity).Name;
    protected string ProtectedCollectionName => CollectionName.ProtectCollectionName();
    internal override string ServerName => _mongoDbService.GetDatabaseHostName();
    internal override string DatabaseName => _mongoDbService.GetDatabaseName();
    public override string CollectionName => _databaseContext?.CollectionName ?? DefaultCollectionName;
    public virtual string DatabasePart => _databaseContext?.CollectionName;
    public override string ConfigurationName => _databaseContext?.ConfigurationName;
    public virtual bool AutoClean => _mongoDbService.GetAutoClean();
    public virtual bool CleanOnStartup => _mongoDbService.GetCleanOnStartup();
    public virtual CreateStrategy CreateCollectionStrategy => _mongoDbService.CreateCollectionStrategy();
    public virtual int? ResultLimit => _mongoDbService.GetResultLimit();
    public virtual IEnumerable<CreateIndexModel<TEntity>> Indices => null;
    internal virtual IEnumerable<CreateIndexModel<TEntity>> CoreIndices => null;
    public virtual IEnumerable<Type> Types => null;

    public abstract IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    public abstract IAsyncEnumerable<TEntity> GetAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    [Obsolete("Projection methods will have to be developed if needed.")]
    public abstract IAsyncEnumerable<T> GetAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, CancellationToken cancellationToken = default) where T : TEntity;

    [Obsolete("Projection methods will have to be developed if needed.")]
    public abstract IAsyncEnumerable<T> GetProjectionAsync<T>(Expression<Func<T, bool>> predicate = null, Options<T> options = null, CancellationToken cancellationToken = default);

    [Obsolete($"Use {nameof(GetManyAsync)} instead. This method will be deprecated.")]
    public abstract Task<Result<TEntity, TKey>> QueryAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    public abstract Task<Result<TEntity, TKey>> GetManyAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    [Obsolete($"Use {nameof(GetManyAsync)} instead. This method will be deprecated.")]
    public abstract Task<Result<TEntity, TKey>> QueryAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    public abstract Task<Result<TEntity, TKey>> GetManyAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    [Obsolete($"Use {nameof(GetManyAsync)} instead. This method will be deprecated.")]
    public abstract IAsyncEnumerable<ResultPage<TEntity, TKey>> GetPagesAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    [Obsolete($"Use {nameof(GetManyAsync)} instead. This method will be deprecated.")]
    public abstract IAsyncEnumerable<ResultPage<TEntity, TKey>> GetPagesAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    public abstract Task<TEntity> GetOneAsync(TKey id, CancellationToken cancellationToken = default);
    public abstract Task<TEntity> GetOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null, CancellationToken cancellationToken = default);
    public abstract Task<TEntity> GetOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null, CancellationToken cancellationToken = default);
    public abstract Task<T> GetOneAsync<T>(Expression<Func<T, bool>> predicate = null, OneOption<T> options = null, CancellationToken cancellationToken = default) where T : TEntity;
    public abstract Task<T> GetOneProjectionAsync<T>(Expression<Func<T, bool>> predicate = null, OneOption<T> options = null, CancellationToken cancellationToken = default);
    public abstract Task AddAsync(TEntity entity);
    public abstract Task<bool> TryAddAsync(TEntity entity);
    public abstract Task AddManyAsync(IEnumerable<TEntity> entities);
    public abstract Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity);
    public abstract Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, OneOption<TEntity> options = null);
    public abstract Task<EntityChangeResult<TEntity>> ReplaceOneAsync(TEntity entity, FilterDefinition<TEntity> filter, OneOption<TEntity> options = null);
    public abstract Task<long> UpdateAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update);
    public abstract Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update);
    public abstract Task<EntityChangeResult<TEntity>> UpdateOneAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update, OneOption<TEntity> options = null);
    public abstract Task<TEntity> DeleteOneAsync(TKey id);
    public abstract Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null);
    public abstract Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate);

    [Obsolete($"Use {nameof(GetCollectionScope)} instead. This method will be deprecated.")]
    public abstract IMongoCollection<TEntity> GetCollection();
    public abstract Task<CollectionScope<TEntity>> GetCollectionScope(Operation operation);

    public abstract Task DropCollectionAsync();

    public abstract Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    public abstract Task<long> CountAsync(FilterDefinition<TEntity> filter, CancellationToken cancellationToken = default);

    public abstract Task<long> GetSizeAsync();

    public abstract IAsyncEnumerable<TEntity> GetDirtyAsync();
    public abstract IEnumerable<(IndexFailOperation Operation, string Name)> GetFailedIndices();

    internal void InvokeAction(ActionEventArgs.ActionData actionData)
    {
        InvokeAction(actionData, _contextData.Value);
    }

    protected virtual Task InitAsync(IMongoCollection<TEntity> collection)
    {
        return Task.CompletedTask;
    }

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

    protected async ValueTask AssureFirewallAccessAsync()
    {
        await _mongoDbService.AssureFirewallAccessAsync(true);
    }
}
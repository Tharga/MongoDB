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

    protected RepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger, DatabaseContext databaseContext = null)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _logger = logger;
        _databaseContext = databaseContext;

        _mongoDbService = mongoDbServiceFactory.GetMongoDbService(() => _databaseContext ?? new DatabaseContext { CollectionName = CollectionName, DatabasePart = DatabasePart, ConfigurationName = ConfigurationName });
        _contextData = new Lazy<ActionEventArgs.ContextData>(BuildContextData);

        _executeInfoLogLevel = _mongoDbService.GetExecuteInfoLogLevel();
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

    //Read
    public abstract IAsyncEnumerable<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    public abstract IAsyncEnumerable<TEntity> GetAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    public abstract IAsyncEnumerable<T> GetProjectionAsync<T>(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    public abstract IAsyncEnumerable<T> GetProjectionAsync<T>(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    public abstract Task<Result<TEntity, TKey>> GetManyAsync(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    public abstract Task<Result<TEntity, TKey>> GetManyAsync(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    public abstract Task<Result<T>> GetManyProjectionAsync<T>(Expression<Func<TEntity, bool>> predicate = null, Options<TEntity> options = null, CancellationToken cancellationToken = default);
    public abstract Task<Result<T>> GetManyProjectionAsync<T>(FilterDefinition<TEntity> filter, Options<TEntity> options = null, CancellationToken cancellationToken = default);

    public abstract Task<TEntity> GetOneAsync(TKey id, CancellationToken cancellationToken = default);
    public abstract Task<TEntity> GetOneAsync(Expression<Func<TEntity, bool>> predicate = null, OneOption<TEntity> options = null, CancellationToken cancellationToken = default);
    public abstract Task<TEntity> GetOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null, CancellationToken cancellationToken = default);

    public abstract Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    public abstract Task<long> CountAsync(FilterDefinition<TEntity> filter, CancellationToken cancellationToken = default);

    public abstract Task<long> GetSizeAsync(CancellationToken cancellationToken = default);

    //Create
    public abstract Task AddAsync(TEntity entity);
    public abstract Task<bool> TryAddAsync(TEntity entity);

    //Update
    public abstract Task<long> UpdateAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update);

    //Delete
    public abstract Task<TEntity> DeleteOneAsync(TKey id);
    public abstract Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate, OneOption<TEntity> options = null);
    public abstract Task<TEntity> DeleteOneAsync(FilterDefinition<TEntity> filter, OneOption<TEntity> options = null);

    public abstract Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate);
    public abstract Task<long> DeleteManyAsync(FilterDefinition<TEntity> filter);

    //Other
    public abstract Task DropCollectionAsync();

    public abstract IAsyncEnumerable<TEntity> GetDirtyAsync();
    public abstract IEnumerable<(IndexFailOperation Operation, string Name)> GetFailedIndices();

    internal abstract Task<StepResponse<IMongoCollection<TEntity>>> FetchCollectionAsync(bool initiate = true);
    internal abstract Task<bool> AssureIndex(IMongoCollection<TEntity> collection, bool forceAssure = false, bool throwOnException = false);
    internal abstract Task<(int Before, int After)> DropIndex(IMongoCollection<TEntity> collection);
    internal abstract Task CleanAsync(IMongoCollection<TEntity> collection);

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

    //protected async ValueTask AssureFirewallAccessAsync()
    //{
    //    await _mongoDbService.AssureFirewallAccessAsync(true);
    //}
}
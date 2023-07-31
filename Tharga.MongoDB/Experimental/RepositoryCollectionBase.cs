using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB.Experimental;

public abstract class RepositoryCollectionBase<TEntity, TKey> : RepositoryCollectionBase
    where TEntity : EntityBase<TKey>
{
    private readonly Lazy<ActionEventArgs.ContextData> _contextData;

    internal readonly ILogger<RepositoryCollectionBase<TEntity, TKey>> _logger;
    internal readonly DatabaseContext _databaseContext;
    internal readonly IMongoDbService _mongoDbService;

    protected RepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger, DatabaseContext databaseContext)
    {
        _logger = logger;
        _databaseContext = databaseContext;

        _mongoDbService = mongoDbServiceFactory.GetMongoDbService(() => _databaseContext ?? new DatabaseContext { CollectionName = CollectionName, DatabasePart = DatabasePart, ConfigurationName = ConfigurationName });
        _contextData = new Lazy<ActionEventArgs.ContextData>(BuildContextData);
    }

    private string DefaultCollectionName => typeof(TEntity).Name;
    protected string ProtectedCollectionName => CollectionName.ProtectCollectionName();

    internal virtual string ServerName => _mongoDbService.GetDatabaseHostName();
    internal virtual string DatabaseName => _mongoDbService.GetDatabaseName();
    public virtual string CollectionName => _databaseContext?.CollectionName ?? DefaultCollectionName;
    public virtual string DatabasePart => _databaseContext?.CollectionName;
    public virtual string ConfigurationName => _databaseContext?.ConfigurationName.Value;
    public virtual int? ResultLimit => _mongoDbService.GetResultLimit();
    public virtual IEnumerable<Type> Types => null;

    internal async Task<T> Execute<T>(string functionName, Func<Task<T>> action, bool assureIndex)
    {
        var sw = new Stopwatch();
        sw.Start();

        try
        {
            if (assureIndex)
            {
                await AssureIndex();
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

    internal void InvokeAction(ActionEventArgs.ActionData actionData)
    {
        InvokeAction(actionData, _contextData.Value);
    }

    public abstract Task<long> GetSizeAsync();
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

    internal abstract Task AssureIndex();
}

public abstract class RepositoryCollectionBase
{
    public static event EventHandler<ActionEventArgs> ActionEvent;

    internal void InvokeAction(ActionEventArgs.ActionData actionData, ActionEventArgs.ContextData contextData)
    {
        ActionEvent?.Invoke(this, new ActionEventArgs(actionData, contextData));
    }
}
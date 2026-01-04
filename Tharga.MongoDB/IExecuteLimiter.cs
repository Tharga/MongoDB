using MongoDB.Driver;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Tharga.MongoDB;

public abstract class ExecuteEventArgs
{
    protected ExecuteEventArgs(Guid executeId)
    {
        ExecuteId = executeId;
    }

    public Guid ExecuteId { get; }
}

public class ExecuteQueuedEventArgs : ExecuteEventArgs
{
    public ExecuteQueuedEventArgs(Guid executeId, int queueCount)
        :base(executeId)
    {
        QueueCount = queueCount;
    }

    public int QueueCount { get; }
}

public class ExecuteStartEventArgs : ExecuteEventArgs
{
    public ExecuteStartEventArgs(Guid executeId, TimeSpan queueTime, bool isCanceled)
        : base(executeId)
    {
        QueueTime = queueTime;
        IsCanceled = isCanceled;
    }

    public TimeSpan QueueTime { get; }
    public bool IsCanceled { get; }
}

public class ExecuteCompleteEventArgs : ExecuteStartEventArgs
{
    public ExecuteCompleteEventArgs(Guid executeId, TimeSpan queueTime, TimeSpan executeTime, bool isCanceled, Exception exception)
        : base(executeId, queueTime, isCanceled)
    {
        ExecuteTime = executeTime;
        Exception = exception;
    }

    public TimeSpan ExecuteTime { get; }
    public TimeSpan TotalTime => QueueTime + ExecuteTime;
    public Exception Exception { get; }
}

internal interface IExecuteLimiter
{
    event EventHandler<ExecuteQueuedEventArgs> ExecuteQueuedEvent;
    event EventHandler<ExecuteStartEventArgs> ExecuteStartEvent;
    event EventHandler<ExecuteCompleteEventArgs> ExecuteCompleteEvent;

    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, string key = default, CancellationToken cancellationToken = default);
}

internal interface ICollectionPool
{
    void AddCollection<TEntity>(string fullName, TEntity collection);
    bool TryGetCollection<TEntity>(string fullName, out IMongoCollection<TEntity> collection);
}

internal class CollectionPool : ICollectionPool
{
    private readonly ConcurrentDictionary<string, object> _collections = new();

    public void AddCollection<TEntity>(string fullName, TEntity collection)
    {
        if (!_collections.TryAdd(fullName, collection)) throw new InvalidOperationException($"Cannot add collection '{fullName}' to the collection pool, it has already been added. This call should be protected and thread safe. Something is wrong in the Tharga.MongoDB package.");
    }

    public bool TryGetCollection<TEntity>(string fullName, out IMongoCollection<TEntity> collection)
    {
        collection = default;
        if (!_collections.TryGetValue(fullName, out var item)) return false;

        collection = (IMongoCollection<TEntity>)item;
        return true;
    }
}
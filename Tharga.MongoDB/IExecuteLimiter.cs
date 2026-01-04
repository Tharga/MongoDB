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

public sealed class ExecuteStartEventArgs : EventArgs
{
    public ExecuteStartEventArgs(Guid executeId, TimeSpan queueElapsed, int concurrentCount)
    {
        ExecuteId = executeId;
        QueueElapsed = queueElapsed;
        ConcurrentCount = concurrentCount;
    }

    public Guid ExecuteId { get; }
    public TimeSpan QueueElapsed { get; }
    public int ConcurrentCount { get; }
}

public class ExecuteCompleteEventArgs
{
    public ExecuteCompleteEventArgs(Guid executeId, TimeSpan queueElapsed, TimeSpan executeElapsed, bool isCanceled, Exception exception)
    {
        ExecuteId = executeId;
        QueueElapsed = queueElapsed;
        ExecuteElapsed = executeElapsed;
        IsCanceled = isCanceled;
        Exception = exception;
    }

    public Guid ExecuteId { get; }
    public TimeSpan QueueElapsed { get; }
    public TimeSpan ExecuteElapsed { get; }
    public TimeSpan TotalTime => QueueElapsed + ExecuteElapsed;
    public bool IsCanceled { get; }
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
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tharga.MongoDB;

public abstract class ExecuteEventArgs
{
    public ExecuteEventArgs(Guid executeId)
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
    public ExecuteStartEventArgs(Guid executeId, TimeSpan queueTime)
        : base(executeId)
    {
        QueueTime = queueTime;
    }

    public TimeSpan QueueTime { get; }
}

public class ExecuteCompleteEventArgs : ExecuteEventArgs
{
    public ExecuteCompleteEventArgs(Guid executeId, TimeSpan queueTime, TimeSpan executeTime)
        : base(executeId)
    {
        QueueTime = queueTime;
        ExecuteTime = executeTime;
    }

    public TimeSpan QueueTime { get; }
    public TimeSpan ExecuteTime { get; }
    public TimeSpan TotalTime => QueueTime + ExecuteTime;
}

internal interface IExecuteLimiter
{
    event EventHandler<ExecuteQueuedEventArgs> ExecuteQueuedEvent;
    event EventHandler<ExecuteStartEventArgs> ExecuteStartEvent;
    event EventHandler<ExecuteCompleteEventArgs> ExecuteCompleteEvent;

    Task<T> ExecuteAsync<T>(string key, Func<Task<T>> action, CancellationToken cancellationToken = default);
}
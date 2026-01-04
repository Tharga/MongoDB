using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Tharga.MongoDB;

public record ExecuteLimiterOptions
{
    public int MaxConcurrent { get; set; }
}

internal class ExecuteLimiter : IExecuteLimiter
{
    private readonly SemaphoreSlim _semaphoreSlim;
    private int _queuedCount;

    public ExecuteLimiter(IOptions<ExecuteLimiterOptions> options)
    {
        _semaphoreSlim = new(options.Value.MaxConcurrent, options.Value.MaxConcurrent);
    }

    public event EventHandler<ExecuteQueuedEventArgs> ExecuteQueuedEvent;
    public event EventHandler<ExecuteStartEventArgs> ExecuteStartEvent;
    public event EventHandler<ExecuteCompleteEventArgs> ExecuteCompleteEvent;

    public async Task<T> ExecuteAsync<T>(string key, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var executeId = Guid.NewGuid();
        var queuedAt = Stopwatch.GetTimestamp();

        var queueCount = Interlocked.Increment(ref _queuedCount);
        ExecuteQueuedEvent?.Invoke(this, new ExecuteQueuedEventArgs(executeId, queueCount));

        await _semaphoreSlim.WaitAsync(cancellationToken);

        Interlocked.Decrement(ref _queuedCount);

        var startedAt = Stopwatch.GetTimestamp();
        var queueTime = GetElapsed(queuedAt, startedAt);

        ExecuteStartEvent?.Invoke(this, new ExecuteStartEventArgs(executeId, queueTime));

        try
        {
            var result = await action.Invoke();

            var completedAt = Stopwatch.GetTimestamp();
            var executeTime = GetElapsed(startedAt, completedAt);

            ExecuteCompleteEvent?.Invoke(
                this,
                new ExecuteCompleteEventArgs(executeId, queueTime, executeTime)
            );

            return result;
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    private static TimeSpan GetElapsed(long from, long to)
    {
        return TimeSpan.FromSeconds((to - from) / (double)Stopwatch.Frequency);
    }
}

using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Tharga.MongoDB;

public record ExecuteLimiterOptions
{
    public int MaxConcurrent { get; set; } = 20;
}

internal class ExecuteLimiter : IExecuteLimiter
{
    private const string DefaultKey = "Default";

    private readonly int _maxConcurrentPerKey;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();
    private readonly ConcurrentDictionary<string, int> _queuedCounts = new();

    public ExecuteLimiter(IOptions<ExecuteLimiterOptions> options)
    {
        _maxConcurrentPerKey = options.Value.MaxConcurrent;
    }

    public event EventHandler<ExecuteQueuedEventArgs> ExecuteQueuedEvent;
    public event EventHandler<ExecuteStartEventArgs> ExecuteStartEvent;
    public event EventHandler<ExecuteCompleteEventArgs> ExecuteCompleteEvent;

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, string key, CancellationToken cancellationToken)
    {
        key ??= DefaultKey;

        var executeId = Guid.NewGuid();
        var semaphore = _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(_maxConcurrentPerKey, _maxConcurrentPerKey));

        var queuedAt = Stopwatch.GetTimestamp();

        var queueCount = _queuedCounts.AddOrUpdate(key, _ => 1, (_, current) => current + 1);

        ExecuteQueuedEvent?.Invoke(this, new ExecuteQueuedEventArgs(executeId, queueCount));

        try
        {
            await semaphore.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            var canceledAt = Stopwatch.GetTimestamp();
            var queueTime = GetElapsed(queuedAt, canceledAt);

            ExecuteCompleteEvent?.Invoke(this, new ExecuteCompleteEventArgs(executeId, queueTime, TimeSpan.Zero, true, null));

            throw;
        }

        _queuedCounts.AddOrUpdate(key, _ => 0, (_, current) => current - 1);

        var startedAt = Stopwatch.GetTimestamp();
        var queueElapsed = GetElapsed(queuedAt, startedAt);
        var wasCanceledBeforeStart = cancellationToken.IsCancellationRequested;

        ExecuteStartEvent?.Invoke(this, new ExecuteStartEventArgs(executeId, queueElapsed, wasCanceledBeforeStart));

        Exception exception = null;

        try
        {
            return await action.Invoke(cancellationToken);
        }
        catch (Exception ex)
        {
            exception = ex;
            throw;
        }
        finally
        {
            var completedAt = Stopwatch.GetTimestamp();
            var executeTime = GetElapsed(startedAt, completedAt);

            ExecuteCompleteEvent?.Invoke(this, new ExecuteCompleteEventArgs(executeId, queueElapsed, executeTime, cancellationToken.IsCancellationRequested, exception));

            semaphore.Release();
        }
    }

    private static TimeSpan GetElapsed(long from, long to)
    {
        return TimeSpan.FromSeconds((to - from) / (double)Stopwatch.Frequency);
    }
}

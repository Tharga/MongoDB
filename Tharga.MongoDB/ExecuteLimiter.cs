using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Tharga.MongoDB;

public record ExecuteLimiterOptions
{
    public int MaxConcurrent { get; set; } = 20;
}

internal class ExecuteLimiter : IExecuteLimiter
{
    private readonly ILogger<ExecuteLimiter> _logger;
    private const string DefaultKey = "Default";

    private readonly int _maxConcurrentPerKey;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();
    private readonly ConcurrentDictionary<string, int> _queuedCounts = new();

    public ExecuteLimiter(IOptions<ExecuteLimiterOptions> options, ILogger<ExecuteLimiter> logger)
    {
        _logger = logger;
        _maxConcurrentPerKey = options.Value.MaxConcurrent;
    }

    //public event EventHandler<ExecuteQueuedEventArgs> ExecuteQueuedEvent;
    //public event EventHandler<ExecuteStartEventArgs> ExecuteStartEvent;
    //public event EventHandler<ExecuteCompleteEventArgs> ExecuteCompleteEvent;

    public async Task<(T Result, ExecuteInfo Info)> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, string key, CancellationToken cancellationToken)
    {
        key ??= DefaultKey;

        //var executeId = Guid.NewGuid();

        var semaphore = _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(_maxConcurrentPerKey, _maxConcurrentPerKey));

        var queuedAt = Stopwatch.GetTimestamp();

        var queueCount = _queuedCounts.AddOrUpdate(key, _ => 1, (_, current) => current + 1);

        //ExecuteQueuedEvent?.Invoke(this, new ExecuteQueuedEventArgs(executeId, queueCount));
        //TODO: Add count info so that it is picked up by the Measure-count-graph.
        if (queueCount > 1)
        {
            _logger.LogInformation("Queued {queueCount} executions for {key}.", queueCount, key);
        }

        //try
        //{
        await semaphore.WaitAsync(cancellationToken);
        //}
        //catch (OperationCanceledException)
        //{
        //    //var canceledAt = Stopwatch.GetTimestamp();
        //    //var queueTime = GetElapsed(queuedAt, canceledAt);
        //
        //    //ExecuteCompleteEvent?.Invoke(this, new ExecuteCompleteEventArgs(executeId, queueTime, TimeSpan.Zero, true, null));
        //
        //    throw;
        //}

        //var queueCountAfter = _queuedCounts.AddOrUpdate(key, _ => 0, (_, current) => current - 1);

        var startedAt = Stopwatch.GetTimestamp();
        var queueElapsed = GetElapsed(queuedAt, startedAt);

        var concurrentCount = _maxConcurrentPerKey - semaphore.CurrentCount;

        //TODO: Add elapsed info so that it is picked up by the Measure-elapsed-graph.
        if (concurrentCount >= _maxConcurrentPerKey)
        {
            _logger.LogWarning("The maximum number of {count} concurrent executions for {key} has been reached.", concurrentCount, key);
        }

        //ExecuteStartEvent?.Invoke(this, new ExecuteStartEventArgs(executeId, queueElapsed, concurrentCount));

        //Exception exception = null;

        try
        {
            var response = await action.Invoke(cancellationToken);
            return (response, new ExecuteInfo
            {
                QueueElapsed = queueElapsed,
                ConcurrentCount = concurrentCount,
                QueueCount = queueCount
            });
        }
        //catch (Exception ex)
        //{
        //    _logger.LogError(ex, ex.Message);
        //    //exception = ex;
        //    throw;
        //}
        finally
        {
            //var completedAt = Stopwatch.GetTimestamp();
            //var executeElapsed = GetElapsed(startedAt, completedAt);

            //ExecuteCompleteEvent?.Invoke(this, new ExecuteCompleteEventArgs(executeId, queueElapsed, executeElapsed, cancellationToken.IsCancellationRequested, exception));

            semaphore.Release();

            //_logger.LogInformation("Executed {executeId} in {executeElapsed} ms. Queued for {queueElapsed} ms.", executeId, executeElapsed.TotalMilliseconds, queueElapsed.TotalMilliseconds);
        }
    }

    private static TimeSpan GetElapsed(long from, long to)
    {
        return TimeSpan.FromSeconds((to - from) / (double)Stopwatch.Frequency);
    }
}
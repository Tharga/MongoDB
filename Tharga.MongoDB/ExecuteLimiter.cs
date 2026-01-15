using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Tharga.MongoDB;

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

    public async Task<(T Result, ExecuteInfo Info)> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, string key, CancellationToken cancellationToken)
    {
        key ??= DefaultKey;

        var semaphore = _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(_maxConcurrentPerKey, _maxConcurrentPerKey));

        var queuedAt = Stopwatch.GetTimestamp();

        var queueCount = _queuedCounts.AddOrUpdate(key, _ => 1, (_, current) => current + 1);

        LogCount("ExecuteQueue", queueCount);
        if (queueCount > 1)
        {
            _logger?.LogInformation("Queued {queueCount} executions for {key}.", queueCount, key);
        }

        await semaphore.WaitAsync(cancellationToken);

        var startedAt = Stopwatch.GetTimestamp();
        var queueElapsed = GetElapsed(queuedAt, startedAt);

        var concurrentCount = _maxConcurrentPerKey - semaphore.CurrentCount;

        LogCount("ExecuteConcurrent", concurrentCount);
        if (concurrentCount >= _maxConcurrentPerKey)
        {
            _logger?.LogWarning("The maximum number of {count} concurrent executions for {key} has been reached.", concurrentCount, key);
        }

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
        finally
        {
            semaphore.Release();
        }
    }

    private void LogCount(string action, int count)
    {
        var data = new Dictionary<string, object>
        {
            { "Monitor", "MongoDB" },
            { "Method", "Count" }
        };
        var details = System.Text.Json.JsonSerializer.Serialize(data);
        _logger?.LogInformation("Count {Action} as {Count}. {Details}", action, count, details);
    }

    private static TimeSpan GetElapsed(long from, long to)
    {
        return TimeSpan.FromSeconds((to - from) / (double)Stopwatch.Frequency);
    }
}
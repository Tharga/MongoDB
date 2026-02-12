using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.MongoDB;

internal class ExecuteLimiter : IExecuteLimiter
{
    private readonly ILogger<ExecuteLimiter> _logger;
    private const string DefaultKey = "Default";

    private readonly bool _enabled;
    private readonly int _maxConcurrentPerKey;

    private readonly ConcurrentDictionary<string, PerKeyState> _states = new();

    public ExecuteLimiter(IOptions<ExecuteLimiterOptions> options, ILogger<ExecuteLimiter> logger)
    {
        _logger = logger;
        _enabled = options.Value.Enabled;
        _maxConcurrentPerKey = options.Value.MaxConcurrent;
    }

    public async Task<(T Result, ExecuteInfo Info)> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        string key,
        CancellationToken cancellationToken)
    {
        if (!_enabled)
        {
            var result = await action(cancellationToken);
            return (result, new ExecuteInfo { QueueElapsed = TimeSpan.Zero, ConcurrentCount = 0, QueueCount = 0 });
        }

        key ??= DefaultKey;

        var state = _states.GetOrAdd(key, _ => new PerKeyState(_maxConcurrentPerKey));

        var queuedAt = Stopwatch.GetTimestamp();

        // Mark as queued (waiting to acquire a slot)
        var queuedCount = state.IncrementQueued();
        LogCount("ExecuteQueue", queuedCount);

        if (queuedCount > 1)
            _logger?.LogInformation("Queued {queueCount} executions for {key}.", queuedCount, key);

        var acquired = false;

        try
        {
            await state.Semaphore.WaitAsync(cancellationToken);
            acquired = true;

            var startedAt = Stopwatch.GetTimestamp();
            var queueElapsed = GetElapsed(queuedAt, startedAt);

            // No longer queued once we got a slot
            state.DecrementQueued();

            // Now executing
            var executingCount = state.IncrementExecuting();
            LogCount("ExecuteConcurrent", executingCount);

            if (executingCount >= _maxConcurrentPerKey)
                _logger?.LogWarning("The maximum number of {count} concurrent executions for {key} has been reached.", executingCount, key);

            try
            {
                var response = await action(cancellationToken);

                return (response, new ExecuteInfo
                {
                    QueueElapsed = queueElapsed,
                    ConcurrentCount = executingCount,
                    QueueCount = queuedCount
                });
            }
            finally
            {
                state.DecrementExecuting();
                state.Semaphore.Release();
            }
        }
        catch
        {
            // If we never acquired, we are still counted as queued -> remove it
            if (!acquired)
            {
                state.DecrementQueued();
            }

            throw;
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
        _logger?.LogTrace("Count {Action} as {Count}. {Details}", action, count, details);
    }

    private static TimeSpan GetElapsed(long from, long to) => TimeSpan.FromSeconds((to - from) / (double)Stopwatch.Frequency);

    private sealed class PerKeyState
    {
        public SemaphoreSlim Semaphore { get; }

        private int _queued;
        private int _executing;

        public PerKeyState(int maxConcurrent)
        {
            Semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        }

        public int IncrementQueued() => Interlocked.Increment(ref _queued);
        public int DecrementQueued() => Interlocked.Decrement(ref _queued);

        public int IncrementExecuting() => Interlocked.Increment(ref _executing);
        public int DecrementExecuting() => Interlocked.Decrement(ref _executing);
    }
}
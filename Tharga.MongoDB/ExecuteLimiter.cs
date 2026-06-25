using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.MongoDB;

internal class ExecuteLimiter : IExecuteLimiter, IQueueMonitor
{
    private readonly ILogger<ExecuteLimiter> _logger;
    private const int MaxMetricEntries = 500;

    private readonly bool _enabled;
    private readonly int? _maxConcurrentOverride;

    private readonly ConcurrentDictionary<string, PerPoolState> _states = new();
    private readonly ConcurrentDictionary<string, bool> _warnedServerKeys = new();
    private readonly ConcurrentQueue<QueueMetricEventArgs> _metrics = new();

    // Atomic state for polling
    private int _totalQueueCount;
    private int _totalExecutingCount;
    private double _lastWaitTimeMs;

    public event EventHandler<QueueMetricEventArgs> QueueMetricEvent;

    public ExecuteLimiter(IOptions<ExecuteLimiterOptions> options, ILogger<ExecuteLimiter> logger)
    {
        _logger = logger;
        _enabled = options.Value.Enabled;
        _maxConcurrentOverride = options.Value.MaxConcurrent;
    }

    public async Task<(T Result, ExecuteInfo Info)> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, string serverKey, string configurationName, int maxConnectionPoolSize, CancellationToken cancellationToken)
    {
        if (!_enabled)
        {
            var result = await action(cancellationToken);
            return (result, new ExecuteInfo { QueueElapsed = TimeSpan.Zero, ConcurrentCount = 0, QueueCount = 0 });
        }

        var maxConcurrent = _maxConcurrentOverride.HasValue
            ? Math.Min(_maxConcurrentOverride.Value, maxConnectionPoolSize)
            : maxConnectionPoolSize;

        if (_maxConcurrentOverride.HasValue && _maxConcurrentOverride.Value > maxConnectionPoolSize
            && _warnedServerKeys.TryAdd(serverKey, true))
        {
            _logger?.LogWarning("Configured MaxConcurrent ({configured}) exceeds MaxConnectionPoolSize ({poolSize}) for {serverKey}. Capping at {poolSize}.",
                _maxConcurrentOverride.Value, maxConnectionPoolSize, serverKey, maxConnectionPoolSize);
        }

        var state = _states.GetOrAdd(serverKey, _ => new PerPoolState(maxConcurrent));
        state.TagConfiguration(configurationName);

        var queuedAt = Stopwatch.GetTimestamp();

        // Mark as queued (waiting to acquire a slot)
        var queuedCount = state.IncrementQueued();
        Interlocked.Increment(ref _totalQueueCount);
        LogCount("ExecuteQueue", queuedCount);

        if (queuedCount > 1)
        {
            _logger?.LogDebug("Queued {queueCount} executions for {serverKey}.", queuedCount, serverKey);
        }

        RecordMetric(queuedCount, state.GetExecuting(), null);

        var acquired = false;

        try
        {
            await state.Semaphore.WaitAsync(cancellationToken);
            acquired = true;

            var startedAt = Stopwatch.GetTimestamp();
            var queueElapsed = GetElapsed(queuedAt, startedAt);

            // No longer queued once we got a slot
            state.DecrementQueued();
            Interlocked.Decrement(ref _totalQueueCount);

            // Now executing
            var executingCount = state.IncrementExecuting();
            Interlocked.Increment(ref _totalExecutingCount);
            LogCount("ExecuteConcurrent", executingCount);

            if (executingCount >= maxConcurrent)
            {
                _logger?.LogWarning("The maximum number of {count} concurrent executions for {serverKey} has been reached. {queueCount} operations waiting in queue.", executingCount, serverKey, state.GetQueued());
            }

            // Update last wait time atomically (take the max)
            var waitMs = queueElapsed.TotalMilliseconds;
            SpinWait spin = default;
            while (true)
            {
                var current = Volatile.Read(ref _lastWaitTimeMs);
                if (waitMs <= current) break;
                if (Interlocked.CompareExchange(ref _lastWaitTimeMs, waitMs, current) == current) break;
                spin.SpinOnce();
            }
            state.RecordWait(waitMs);

            RecordMetric(state.GetQueued(), executingCount, queueElapsed);

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
                Interlocked.Decrement(ref _totalExecutingCount);
                state.Semaphore.Release();

                RecordMetric(state.GetQueued(), state.GetExecuting(), null);
            }
        }
        catch
        {
            // If we never acquired, we are still counted as queued -> remove it
            if (!acquired)
            {
                state.DecrementQueued();
                Interlocked.Decrement(ref _totalQueueCount);
            }

            throw;
        }
    }

    public IReadOnlyList<QueueMetricEventArgs> GetRecentMetrics()
    {
        return _metrics.ToArray();
    }

    public (int QueueCount, int ExecutingCount, double LastWaitTimeMs) GetCurrentState()
    {
        var waitMs = Interlocked.Exchange(ref _lastWaitTimeMs, 0);
        return (
            Volatile.Read(ref _totalQueueCount),
            Volatile.Read(ref _totalExecutingCount),
            waitMs
        );
    }

    public IReadOnlyList<PoolQueueState> GetPerPoolState()
    {
        var result = new List<PoolQueueState>(_states.Count);
        foreach (var (serverKey, state) in _states)
        {
            result.Add(new PoolQueueState
            {
                ServerKey = serverKey,
                ConfigurationNames = state.GetConfigurations(),
                QueueCount = state.GetQueued(),
                ExecutingCount = state.GetExecuting(),
                LastWaitTimeMs = state.GetAndResetWait(),
            });
        }
        return result;
    }

    private void RecordMetric(int queueCount, int executingCount, TimeSpan? waitTime)
    {
        var metric = new QueueMetricEventArgs
        {
            Timestamp = DateTime.UtcNow,
            QueueCount = queueCount,
            ExecutingCount = executingCount,
            WaitTime = waitTime,
        };

        _metrics.Enqueue(metric);
        while (_metrics.Count > MaxMetricEntries)
            _metrics.TryDequeue(out _);

        // No longer fire event synchronously — consumers poll via GetCurrentState()
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

    private sealed class PerPoolState
    {
        public SemaphoreSlim Semaphore { get; }

        private int _queued;
        private int _executing;
        private double _lastWaitTimeMs;
        private readonly ConcurrentDictionary<string, bool> _configurationNames = new();

        public PerPoolState(int maxConcurrent)
        {
            Semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        }

        public int IncrementQueued() => Interlocked.Increment(ref _queued);
        public int DecrementQueued() => Interlocked.Decrement(ref _queued);
        public int GetQueued() => Volatile.Read(ref _queued);

        public int IncrementExecuting() => Interlocked.Increment(ref _executing);
        public int DecrementExecuting() => Interlocked.Decrement(ref _executing);
        public int GetExecuting() => Volatile.Read(ref _executing);

        public void TagConfiguration(string configurationName)
        {
            if (!string.IsNullOrEmpty(configurationName))
                _configurationNames.TryAdd(configurationName, true);
        }

        public IReadOnlyCollection<string> GetConfigurations() => _configurationNames.Keys.ToArray();

        // Track the wait-time high-water mark since the last read (take the max, reset on read),
        // mirroring the global _lastWaitTimeMs semantics.
        public void RecordWait(double waitMs)
        {
            SpinWait spin = default;
            while (true)
            {
                var current = Volatile.Read(ref _lastWaitTimeMs);
                if (waitMs <= current) break;
                if (Interlocked.CompareExchange(ref _lastWaitTimeMs, waitMs, current) == current) break;
                spin.SpinOnce();
            }
        }

        public double GetAndResetWait() => Interlocked.Exchange(ref _lastWaitTimeMs, 0);
    }
}

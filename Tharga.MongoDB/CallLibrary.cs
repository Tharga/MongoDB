using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Tharga.MongoDB;
using Tharga.MongoDB.Configuration;

internal class CallLibrary : ICallLibrary
{
    private readonly DatabaseOptions _options;
    private readonly ConcurrentQueue<Guid> _recentCalls;
    private readonly ConcurrentDictionary<Guid, CallInfo> _calls;
    private readonly PriorityQueue<CallInfo, double> _slowest;
    private readonly object _slowestLock = new();
    private readonly ConcurrentDictionary<string, int> _callCounts = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _completedAt = new();
    private static readonly TimeSpan CompletedRetention = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromMilliseconds(500);
    private int _throttlePending;
    private long _lastNotifyTicks;

    private readonly Channel<object> _channel;
    private readonly Task _consumerTask;

    public event EventHandler CallChanged;

    public CallLibrary(IOptions<DatabaseOptions> options)
    {
        _options = options.Value;
        _recentCalls = new ConcurrentQueue<Guid>();
        _calls = new ConcurrentDictionary<Guid, CallInfo>();
        _slowest = new PriorityQueue<CallInfo, double>();

        _channel = Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });
        _consumerTask = Task.Run(ProcessChannelAsync);
    }

    public void StartCall(CallStartEventArgs e)
    {
        var info = new CallInfo
        {
            Key = e.CallKey,
            StartTime = DateTime.UtcNow,
            Fingerprint = e.Fingerprint,
            FunctionName = e.FunctionName,
            Operation = e.Operation
        };

        _calls.TryAdd(e.CallKey, info);
        _recentCalls.Enqueue(e.CallKey);

        while (_recentCalls.Count > _options.Monitor.LastCallsToKeep)
        {
            if (_recentCalls.TryDequeue(out var oldKey))
            {
                _calls.TryRemove(oldKey, out _);
            }
        }

        _callCounts.AddOrUpdate(e.Fingerprint.Key, 1, (_, count) => count + 1);

        // Queue notification (don't process heavy work inline)
        _channel.Writer.TryWrite(e);
    }

    public void EndCall(CallEndEventArgs e)
    {
        // Update call info inline (lightweight)
        if (_calls.TryGetValue(e.CallKey, out var item))
        {
            item.Elapsed = e.Elapsed;
            item.Count = e.Count;
            item.Exception = e.Exception;
            item.Final = e.Final;
            item.Steps = e.Steps;
            item.FilterJsonProvider = e.FilterJsonProvider;
            item.ExplainProvider = e.ExplainProvider;

            if (e.Final)
            {
                _completedAt.TryAdd(e.CallKey, DateTime.UtcNow);
            }
        }

        // Queue slow-call tracking + notification to background consumer
        _channel.Writer.TryWrite(e);
    }

    public IEnumerable<CallInfo> GetLastCalls()
    {
        return _calls.Values;
    }

    public IEnumerable<CallInfo> GetSlowCalls()
    {
        lock (_slowestLock)
        {
            return _slowest.UnorderedItems.Select(x => x.Element).ToList();
        }
    }

    public IEnumerable<CallInfo> GetOngoingCalls()
    {
        var cutoff = DateTime.UtcNow - CompletedRetention;

        // Purge expired completed calls
        foreach (var kvp in _completedAt)
        {
            if (kvp.Value < cutoff)
            {
                _completedAt.TryRemove(kvp.Key, out _);
            }
        }

        // Return ongoing + recently completed
        return _calls.Values.Where(x => !x.Final || _completedAt.ContainsKey(x.Key));
    }

    public CallInfo GetCall(Guid key)
    {
        if (_calls.TryGetValue(key, out var value)) return value;
        lock (_slowestLock)
        {
            var item = _slowest.UnorderedItems.FirstOrDefault(x => x.Element.Key == key);
            return item.Element;
        }
    }

    public IReadOnlyDictionary<string, int> GetCallCounts()
    {
        return new ReadOnlyDictionary<string, int>(_callCounts);
    }

    public void ResetCalls()
    {
        _calls.Clear();
        while (_recentCalls.TryDequeue(out _)) { }
        lock (_slowestLock)
        {
            _slowest.Clear();
        }
        _callCounts.Clear();
        _completedAt.Clear();

        NotifyChanged(immediate: true);
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        // Don't block on consumer — it will drain naturally
    }

    private async Task ProcessChannelAsync()
    {
        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync())
            {
                try
                {
                    switch (item)
                    {
                        case CallStartEventArgs:
                            NotifyChanged();
                            break;

                        case CallEndEventArgs endEvent:
                            ProcessEndCall(endEvent);
                            NotifyChanged();
                            break;
                    }
                }
                catch
                {
                    // Swallow exceptions to keep consumer alive
                }
            }
        }
        catch (ChannelClosedException)
        {
            // Channel completed, consumer exits
        }
    }

    private void ProcessEndCall(CallEndEventArgs e)
    {
        if (!_calls.TryGetValue(e.CallKey, out var item)) return;

        var elapsedMs = e.Elapsed.TotalMilliseconds;

        lock (_slowestLock)
        {
            if (_slowest.Count < _options.Monitor.SlowCallsToKeep)
            {
                _slowest.Enqueue(item, elapsedMs);
            }
            else if (_slowest.TryPeek(out _, out var smallest))
            {
                if (elapsedMs > smallest)
                {
                    _slowest.Dequeue();
                    _slowest.Enqueue(item, elapsedMs);
                }
            }
        }
    }

    private void NotifyChanged(bool immediate = false)
    {
        if (immediate)
        {
            Interlocked.Exchange(ref _lastNotifyTicks, DateTime.UtcNow.Ticks);
            CallChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastNotifyTicks);
        if (now - last >= ThrottleInterval.Ticks)
        {
            Interlocked.Exchange(ref _lastNotifyTicks, now);
            CallChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (Interlocked.CompareExchange(ref _throttlePending, 1, 0) == 0)
        {
            _ = Task.Delay(ThrottleInterval).ContinueWith(_ =>
            {
                Interlocked.Exchange(ref _throttlePending, 0);
                Interlocked.Exchange(ref _lastNotifyTicks, DateTime.UtcNow.Ticks);
                CallChanged?.Invoke(this, EventArgs.Empty);
            });
        }
    }
}

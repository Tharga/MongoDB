using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB;
using Tharga.MongoDB.Configuration;

internal class CallLibrary : ICallLibrary
{
    private readonly DatabaseOptions _options;
    private readonly ConcurrentQueue<Guid> _recentCalls;
    private readonly ConcurrentDictionary<Guid, CallInfo> _calls;
    private readonly PriorityQueue<CallInfo, double> _slowest;
    private readonly SemaphoreSlim _slowestSemaphore = new(1, 1);

    public CallLibrary(IOptions<DatabaseOptions> options)
    {
        _options = options.Value;
        _recentCalls = new ConcurrentQueue<Guid>();
        _calls = new ConcurrentDictionary<Guid, CallInfo>();
        _slowest = new PriorityQueue<CallInfo, double>();
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
    }

    public async Task<CollectionFingerprint> EndCallAsync(CallEndEventArgs e)
    {
        if (!_calls.TryGetValue(e.CallKey, out var item)) return default;

        item.Elapsed = e.Elapsed;
        item.Count = e.Count;
        item.Exception = e.Exception;
        item.Final = e.Final;
        item.Steps = e.Steps;
        item.FilterJson = e.FilterJson;

        var elapsedMs = e.Elapsed.TotalMilliseconds;

        await _slowestSemaphore.WaitAsync().ConfigureAwait(false);
        try
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
        finally
        {
            _slowestSemaphore.Release();
        }

        return item.Fingerprint;
    }

    public IEnumerable<CallInfo> GetLastCalls()
    {
        return _calls.Values;
    }

    public IEnumerable<CallInfo> GetSlowCalls()
    {
        _slowestSemaphore.Wait();
        try
        {
            return _slowest.UnorderedItems.Select(x => x.Element);
        }
        finally
        {
            _slowestSemaphore.Release();
        }
    }

    public IEnumerable<CallInfo> GetOngoingCalls()
    {
        return _calls.Values.Where(x => !x.Final);
    }

    public CallInfo GetCall(Guid key)
    {
        return _calls[key];
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

internal class CallLibrary : ICallLibrary
{
    private readonly DatabaseOptions _options;
    private readonly ConcurrentQueue<Guid> _recentCalls;
    private readonly ConcurrentDictionary<Guid, CallInfo> _calls;
    private readonly ConcurrentQueue<CallInfo> _slowCalls;

    public CallLibrary(IOptions<DatabaseOptions> options)
    {
        _options = options.Value;
        _recentCalls = new ConcurrentQueue<Guid>();
        _calls = new ConcurrentDictionary<Guid, CallInfo>();
        _slowCalls = new ConcurrentQueue<CallInfo>();
    }

    public void StartCall(CallStartEventArgs e)
    {
        _calls.TryAdd(e.CallKey, new CallInfo
        {
            StartTime = DateTime.UtcNow,
            CollectionName = e.CollectionName,
            FunctionName = e.FunctionName,
            Operation = e.Operation
        });

        _recentCalls.Enqueue(e.CallKey);

        while (_recentCalls.Count > _options.Monitor.LastCallsToKeep)
        {
            if (_recentCalls.TryDequeue(out var oldKey))
            {
                _calls.TryRemove(oldKey, out _);
            }
        }
    }

    public void EndCall(CallEndEventArgs e)
    {
        if (_calls.TryGetValue(e.CallKey, out var item))
        {
            item.Elapsed = e.Elapsed;
            item.Count = e.Count;
            item.Exception = e.Exception;
            item.Final = e.Final;
        }

        if (e.Elapsed > _options.Monitor.SlowCallThreshold)
        {
            if (_calls.TryGetValue(e.CallKey, out var slow))
            {
                _slowCalls.Enqueue(slow);

                while (_slowCalls.Count > _options.Monitor.SlowCallsToKeep)
                {
                    _slowCalls.TryDequeue(out _);
                }
            }
        }
    }

    public IEnumerable<CallInfo> GetLastCalls()
    {
        return _calls.Values;
    }

    public IEnumerable<CallInfo> GetSlowCalls()
    {
        return _slowCalls;
    }
}
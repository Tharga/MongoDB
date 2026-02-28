using System;
using System.Collections.Generic;

namespace Tharga.MongoDB;

public class CallEndEventArgs : EventArgs
{
    public CallEndEventArgs(Guid callKey, TimeSpan elapsed, Exception exception, int count, IReadOnlyList<CallStepInfo> steps = null, string filterJson = null, bool final = true)
    {
        CallKey = callKey;
        Elapsed = elapsed;
        Exception = exception;
        Count = count;
        Steps = steps;
        FilterJson = filterJson;
        Final = final;
    }

    public Guid CallKey { get; }
    public TimeSpan Elapsed { get; }
    public int Count { get; }
    public bool Final { get; }
    public Exception Exception { get; }
    public IReadOnlyList<CallStepInfo> Steps { get; }
    public string FilterJson { get; }
}
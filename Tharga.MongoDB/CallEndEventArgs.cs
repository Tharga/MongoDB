using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tharga.MongoDB;

public class CallEndEventArgs : EventArgs
{
    public CallEndEventArgs(Guid callKey, TimeSpan elapsed, Exception exception, int count, IReadOnlyList<CallStepInfo> steps = null, Func<string> filterJsonProvider = null, Func<CancellationToken, Task<string>> explainProvider = null, bool final = true)
    {
        CallKey = callKey;
        Elapsed = elapsed;
        Exception = exception;
        Count = count;
        Steps = steps;
        FilterJsonProvider = filterJsonProvider;
        ExplainProvider = explainProvider;
        Final = final;
    }

    public Guid CallKey { get; }
    public TimeSpan Elapsed { get; }
    public int Count { get; }
    public bool Final { get; }
    public Exception Exception { get; }
    public IReadOnlyList<CallStepInfo> Steps { get; }
    public Func<string> FilterJsonProvider { get; }
    public Func<CancellationToken, Task<string>> ExplainProvider { get; }
}
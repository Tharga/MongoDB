using System;

namespace Tharga.MongoDB;

public class CallEndEventArgs : EventArgs
{
    public CallEndEventArgs(Guid callKey, TimeSpan elapsed, Exception exception, int count, bool final, string explain)
    {
        CallKey = callKey;
        Elapsed = elapsed;
        Exception = exception;
        Count = count;
        Final = final;
        Explain = explain;
    }

    public Guid CallKey { get; }
    public TimeSpan Elapsed { get; }
    public Exception Exception { get; }
    public int Count { get; }
    public bool Final { get; }
    public string Explain { get; }
}
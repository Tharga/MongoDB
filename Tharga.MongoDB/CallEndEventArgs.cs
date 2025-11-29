using System;

namespace Tharga.MongoDB;

public class CallEndEventArgs : EventArgs
{
    public CallEndEventArgs(Guid callKey, TimeSpan elapsed, Exception exception, int count, bool final = true)
    {
        CallKey = callKey;
        Elapsed = elapsed;
        Exception = exception;
        Count = count;
        Final = final;
    }

    public Guid CallKey { get; }
    public TimeSpan Elapsed { get; }
    public int Count { get; }
    public bool Final { get; }
    public Exception Exception { get; }
}
using System;

namespace Tharga.MongoDB;

public class CallEndEventArgs : EventArgs
{
    public CallEndEventArgs(Guid callKey, TimeSpan elapsed, Exception exception)
    {
        CallKey = callKey;
        Elapsed = elapsed;
        Exception = exception;
    }

    public Guid CallKey { get; }
    public TimeSpan Elapsed { get; }
    public Exception Exception { get; }
}
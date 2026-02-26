using System;

namespace Tharga.MongoDB.Lockable;

public sealed class LockExpiredException : InvalidOperationException
{
    public LockExpiredException(string message, TimeSpan timeout, TimeSpan lockTime)
        : base(message)
    {
        Data["timeout"] = timeout;
        Data["lockTime"] = lockTime;
    }
}
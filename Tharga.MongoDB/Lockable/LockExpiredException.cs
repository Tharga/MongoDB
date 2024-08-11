using System;

namespace Tharga.MongoDB.Lockable;

public class LockExpiredException : InvalidOperationException
{
    public LockExpiredException(string message)
        : base(message)
    {
    }
}
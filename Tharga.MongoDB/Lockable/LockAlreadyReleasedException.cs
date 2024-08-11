using System;

namespace Tharga.MongoDB.Lockable;

public class LockAlreadyReleasedException : InvalidOperationException
{
    public LockAlreadyReleasedException(string message)
        : base(message)
    {
    }
}
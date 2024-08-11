using System;

namespace Tharga.MongoDB.Lockable;

public class UnlockDifferentEntityException : InvalidOperationException
{
    public UnlockDifferentEntityException(string message)
        : base(message)
    {
    }
}
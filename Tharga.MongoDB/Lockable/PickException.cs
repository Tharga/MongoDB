using System;

namespace Tharga.MongoDB.Lockable;

public abstract class PickException : Exception
{
    protected PickException(string message)
        : base(message)
    {
    }
}
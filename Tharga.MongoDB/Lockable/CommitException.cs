using System;

namespace Tharga.MongoDB.Lockable;

public class CommitException : InvalidOperationException
{
    public CommitException(Exception exception)
        : base($"Failed to commit. {exception.Message}", exception)
    {
    }
}
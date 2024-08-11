using System;

namespace Tharga.MongoDB.Lockable;

public class DeleteException : InvalidOperationException
{
    public DeleteException()
        : base("Cannot delete locked entities.")
    {
    }
}
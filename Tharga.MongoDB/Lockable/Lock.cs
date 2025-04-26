using System;
using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.MongoDB.Lockable;

public record Lock
{
    internal Lock()
    {
    }

    public required Guid LockKey { get; init; }
    public required DateTime LockTime { get; init; }
    public required DateTime ExpireTime { get; init; }

    [BsonIgnoreIfDefault]
    public string Actor { get; init; }

    [BsonIgnoreIfDefault]
    public ExceptionInfo ExceptionInfo { get; init; }
}
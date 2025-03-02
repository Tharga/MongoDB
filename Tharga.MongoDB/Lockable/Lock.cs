using System;
using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.MongoDB.Lockable;

public record Lock
{
    public Guid LockKey { get; internal init; }
    public DateTime LockTime { get; internal init; }

    [BsonIgnoreIfDefault]
    public DateTime? ExpireTime { get; internal init; }

    [BsonIgnoreIfDefault]
    public string Actor { get; internal init; }

    [BsonIgnoreIfDefault]
    public ExceptionInfo ExceptionInfo { get; internal init; }
}
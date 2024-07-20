using System;
using MongoDB.Bson;
using Tharga.MongoDB.Compress;

namespace Tharga.MongoDB.Tests.Support;

public record CompressTestEntity : CompressEntityBase<CompressTestEntity, ObjectId>
{
    public override string AggregateKey { get; }
    public override CompressTestEntity Merge(CompressTestEntity other)
    {
        throw new NotImplementedException();
    }
}
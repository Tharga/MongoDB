using MongoDB.Bson;

namespace Tharga.MongoDB.Tests.Support;

public record TestEntity : EntityBase<ObjectId>
{
    public string Value { get; set; }
}
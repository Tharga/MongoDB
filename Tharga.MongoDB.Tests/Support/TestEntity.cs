using MongoDB.Bson;

namespace Tharga.MongoDB.Tests.Support;

public record TestEntity : EntityBase<ObjectId>
{
    public string Value { get; set; }
}

public record TestSubEntity : TestEntity
{
    public string OtherValue { get; set; }
}

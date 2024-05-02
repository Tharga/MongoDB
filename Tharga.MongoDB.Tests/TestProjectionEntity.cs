using MongoDB.Bson;

namespace Tharga.MongoDB.Tests;

public record TestProjectionEntity : EntityBase<ObjectId>
{
    public string Value { get; set; }
}
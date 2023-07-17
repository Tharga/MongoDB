using MongoDB.Bson;
using Tharga.MongoDB;

namespace HostSample.Entities;

public record MyEntity : EntityBase<ObjectId>
{
    public int Counter { get; set; }
}

public abstract record MyEntityBase : EntityBase<ObjectId>;

public record MyFirstEntity : MyEntityBase
{
    public string Value { get; set; }
}

public record MySecondEntity : MyEntityBase
{
    public string OtherValue { get; set; }
}
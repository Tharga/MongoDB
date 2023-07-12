using MongoDB.Bson;

namespace Tharga.MongoDB.HostSample.Entities;

public record MyEntity : EntityBase<ObjectId>
{
    public int Counter { get; set; }
}
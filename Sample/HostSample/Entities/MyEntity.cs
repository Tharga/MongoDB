using MongoDB.Bson;
using Tharga.MongoDB;

namespace HostSample.Entities;

public record MyEntity : EntityBase<ObjectId>
{
    public int Counter { get; set; }
}
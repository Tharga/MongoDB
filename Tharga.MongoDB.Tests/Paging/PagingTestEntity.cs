using MongoDB.Bson;

namespace Tharga.MongoDB.Tests.Paging;

public record PagingTestEntity : EntityBase<ObjectId>
{
    public string Name { get; init; }
    public int Bucket { get; init; }
}

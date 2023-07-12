using MongoDB.Bson;

namespace Tharga.MongoDB.ConsoleSample.SampleRepo;

public interface IMyRepo : IRepositoryCollection<MyBaseEntity, ObjectId>
{
}
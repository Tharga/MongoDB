using MongoDB.Bson;
using Tharga.MongoDB;

namespace ConsoleSample.SampleRepo;

public interface IMyRepo : IDiskRepositoryCollection<MyBaseEntity, ObjectId>
{
}
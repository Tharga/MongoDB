using MongoDB.Bson;
using Tharga.MongoDB.HostSample.Entities;

namespace Tharga.MongoDB.HostSample.Features.BasicDiskRepo;

public interface IMyBasicDiskRepoCollection : IRepositoryCollection<MyEntity, ObjectId>
{
}
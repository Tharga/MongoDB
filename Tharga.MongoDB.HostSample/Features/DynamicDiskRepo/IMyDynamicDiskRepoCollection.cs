using MongoDB.Bson;
using Tharga.MongoDB.HostSample.Entities;

namespace Tharga.MongoDB.HostSample.Features.DynamicDiskRepo;

public interface IMyDynamicDiskRepoCollection : IRepositoryCollection<MyEntity, ObjectId>
{
}
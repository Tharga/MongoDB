using HostSample.Entities;
using MongoDB.Bson;
using Tharga.MongoDB;

namespace HostSample.Features.DynamicDiskRepo;

public interface IMyDynamicDiskRepoCollection : IRepositoryCollection<MyEntity, ObjectId>
{
}
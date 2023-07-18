using HostSample.Entities;
using MongoDB.Bson;
using Tharga.MongoDB;

namespace HostSample.Features.MultiTypeDiskRepo;

public interface IMyMultiTypeDiskRepoCollection : IRepositoryCollection<MyEntityBase, ObjectId>
{
}
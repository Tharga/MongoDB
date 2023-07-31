using HostSample.Entities;
using MongoDB.Bson;
using Tharga.MongoDB;

namespace HostSample.Features.BasicDiskRepo;

public interface IMyBasicDiskRepoCollection : IDiskRepositoryCollection<MyEntity, ObjectId>
{
}
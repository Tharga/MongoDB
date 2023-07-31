using HostSample.Entities;
using MongoDB.Bson;
using Tharga.MongoDB;

namespace HostSample.Features.Experimental;

public interface IExperimentalDiskRepoCollection : IDiskRepositoryCollection<MyEntity, ObjectId>
{
}
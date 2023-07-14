using System.Collections.Generic;
using System.Threading.Tasks;
using HostSample.Entities;
using MongoDB.Bson;
using Tharga.MongoDB;

namespace HostSample.Features.DynamicDiskRepo;

public interface IMyDynamicDiskRepoRepo : IRepository
{
    IAsyncEnumerable<MyEntity> GetAll(string configurationName, string collectionName, string databasePart);
    Task<MyEntity> GetOne(ObjectId id, string configurationName, string collectionName, string databasePart);
    Task<ObjectId> CreateRandom(string configurationName, string collectionName, string databasePart);
    Task<int> Count(ObjectId objectId, string configurationName, string collectionName, string databasePart);
}
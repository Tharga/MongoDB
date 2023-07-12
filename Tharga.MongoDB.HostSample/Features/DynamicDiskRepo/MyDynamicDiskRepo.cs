using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.HostSample.Entities;

namespace Tharga.MongoDB.HostSample.Features.DynamicDiskRepo;

public class MyDynamicDiskRepo : IMyDynamicDiskRepoRepo
{
    private readonly ICollectionProvider _provider;

    public MyDynamicDiskRepo(ICollectionProvider provider)
    {
        _provider = provider;
    }

    public IAsyncEnumerable<MyEntity> GetAll(string configurationName, string collectionName, string databasePart)
    {
        var collection = GetCollection(configurationName, collectionName, databasePart);
        return collection.GetAsync(x => true);
    }

    public Task<MyEntity> GetOne(ObjectId id, string configurationName, string collectionName, string databasePart)
    {
        var collection = GetCollection(configurationName, collectionName, databasePart);
        return collection.GetOneAsync(x => x.Id == id);
    }

    public async Task<ObjectId> CreateRandom(string configurationName, string collectionName, string databasePart)
    {
        var collection = GetCollection(configurationName, collectionName, databasePart);
        var myEntity = new MyEntity();
        await collection.AddAsync(myEntity);
        return myEntity.Id;

    }

    public async Task<int> Count(ObjectId objectId, string configurationName, string collectionName, string databasePart)
    {
        var collection = GetCollection(configurationName, collectionName, databasePart);
        var updateDefinition = new UpdateDefinitionBuilder<MyEntity>().Inc(x => x.Counter, 1);
        var result = await collection.UpdateOneAsync(objectId, updateDefinition);
        var item = await result.GetAfterAsync();
        return item.Counter;
    }

    private IMyDynamicDiskRepoCollection GetCollection(string configurationName, string collectionName, string databasePart)
    {
        return _provider.GetCollection<IMyDynamicDiskRepoCollection, MyEntity, ObjectId>(new DatabaseContext
        {
            CollectionName = collectionName ?? "MyCollection",
            DatabasePart = databasePart ?? "MyDatabasePart",
            ConfigurationName = configurationName
        });
    }
}
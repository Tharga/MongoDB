using System.Collections.Generic;
using System.Threading.Tasks;
using HostSample.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HostSample.Features.MultiTypeDiskRepo;

public class MyMultiTypeDiskRepo : IMyMultiTypeDiskRepo
{
    private readonly IMyMultiTypeDiskRepoCollection _collection;

    public MyMultiTypeDiskRepo(IMyMultiTypeDiskRepoCollection collection)
    {
        _collection = collection;
    }

    public IAsyncEnumerable<MyEntityBase> GetAll()
    {
        return _collection.GetAsync(x => true);
    }

    public async IAsyncEnumerable<T> GetByType<T>() where T : MyEntityBase
    {
        var filter = new FilterDefinitionBuilder<MyEntityBase>().OfType<T>();
        await foreach (var item in _collection.GetAsync(filter))
        {
            yield return (T)item;
        }
    }

    public Task CreateRandom<T>(T item) where T : MyEntityBase
    {
        return _collection.AddAsync(item);
    }
}
using System.Collections.Generic;
using System.Threading.Tasks;
using HostSample.Entities;
using MongoDB.Driver;

namespace HostSample.Features.BasicDiskRepo;

public class MyBasicDiskRepo : IMyBasicDiskRepo
{
    private readonly IMyBasicDiskRepoCollection _collection;

    public MyBasicDiskRepo(IMyBasicDiskRepoCollection collection)
    {
        _collection = collection;
    }

    public IAsyncEnumerable<MyEntity> GetAll()
    {
        return _collection.GetAsync(x => true);
    }

    public Task ResetAllCounters()
    {
        var filter = FilterDefinition<MyEntity>.Empty;
        var update = new UpdateDefinitionBuilder<MyEntity>().Set(x => x.Counter, 0);
        return _collection.UpdateAsync(filter, update);
    }

    public Task IncreaseAllCounters()
    {
        var filter = FilterDefinition<MyEntity>.Empty;
        var update = new UpdateDefinitionBuilder<MyEntity>().Inc(x => x.Counter, 1);
        return _collection.UpdateAsync(filter, update);
    }
}
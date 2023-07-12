using System.Collections.Generic;
using Tharga.MongoDB.HostSample.Entities;

namespace Tharga.MongoDB.HostSample.Features.BasicDiskRepo;

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
}
using System.Collections.Generic;
using HostSample.Entities;

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
}
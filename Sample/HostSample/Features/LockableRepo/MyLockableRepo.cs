using System.Collections.Generic;

namespace HostSample.Features.LockableRepo;

public class MyLockableRepo : IMyLockableRepo
{
    private readonly IMyLockableCollection _collection;

    public MyLockableRepo(IMyLockableCollection collection)
    {
        _collection = collection;
    }

    public IAsyncEnumerable<MyLockableEntity> GetAll()
    {
        return _collection.GetAsync(x => true);
    }
}
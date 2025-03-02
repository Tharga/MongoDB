using MongoDB.Bson;

namespace HostSample.Features.LockableRepo;

public class MyLockableRepo : IMyLockableRepo
{
    private readonly IMyLockableCollection _collection;

    public MyLockableRepo(IMyLockableCollection collection)
    {
        _collection = collection;
    }

    public async Task AddAsync(MyLockableEntity myLockableEntity)
    {
        await _collection.AddAsync(myLockableEntity);
    }

    public IAsyncEnumerable<MyLockableEntity> GetAll()
    {
        return _collection.GetAsync(x => true);
    }

    public async Task<MyLockableEntity> BumpCountAsync(ObjectId id)
    {
        var scope = await _collection.GetForUpdateAsync(id);
        scope.Entity.Counter++;
        return await scope.CommitAsync();
    }

    public async Task LockAsync(ObjectId id, TimeSpan timeout, string actor)
    {
        await _collection.GetForUpdateAsync(id, timeout, actor);
    }
}
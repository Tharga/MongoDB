using MongoDB.Bson;
using Tharga.MongoDB.Lockable;
using Exception = System.Exception;

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

    public IAsyncEnumerable<MyLockableEntity> GetUnlocked()
    {
        return _collection.GetUnlocked(x => true);
    }

    public async Task<MyLockableEntity> BumpCountAsync(ObjectId id)
    {
        var scope = await _collection.PickForUpdateAsync(id);
        scope.Entity.Counter++;
        return await scope.CommitAsync();
    }

    public async Task ThrowAsync(ObjectId id)
    {
        var scope = await _collection.PickForUpdateAsync(id);
        scope.Entity.Counter++;
        await scope.SetErrorStateAsync(new Exception("Some issue."));
    }

    public async Task LockAsync(ObjectId id, TimeSpan timeout, string actor)
    {
        await _collection.PickForUpdateAsync(id, timeout, actor);
    }

    public async Task<bool> UnlockAsync(ObjectId id)
    {
        return await _collection.ReleaseAsync(id, ReleaseMode.Any);
    }

    public Task<long> DeleteAllAsync()
    {
        return _collection.DeleteManyAsync(x => true);
    }

    public IAsyncEnumerable<EntityLock<MyLockableEntity, ObjectId>> GetLockedAsync(LockMode mode)
    {
        return _collection.GetLockedAsync(mode);
    }
}
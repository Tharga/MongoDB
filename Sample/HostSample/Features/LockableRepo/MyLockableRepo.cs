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

    public IAsyncEnumerable<MyLockableEntity> GetUnlockedAsync()
    {
        return _collection.GetUnlockedAsync(x => true);
    }

    public async Task<MyLockableEntity> BumpCountAsync(ObjectId id)
    {
        var scope = await _collection.PickForUpdateAsync(id);
        scope.Entity.Counter++;
        return await scope.CommitAsync();
    }

    public async Task<MyLockableEntity> ProcessLongRunningAsync(ObjectId id, int steps)
    {
        // Take a short lock, then "buy more time" as the job progresses. ExtendLockAsync is safe to call
        // every iteration — it writes to the database at most once per MinLockExtendInterval (default 60s).
        await using var scope = await _collection.PickForUpdateAsync(id, timeout: TimeSpan.FromMinutes(5), actor: "long-runner");

        for (var i = 0; i < steps; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50)); // stand-in for an irregular unit of work
            scope.Entity.Counter++;

            var extension = await scope.ExtendLockAsync(TimeSpan.FromMinutes(5));
            if (!extension.Extended)
            {
                // Throttled (still well within the previous expiry) — nothing written this iteration.
            }
        }

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
        await _collection.ReleaseOneAsync(id, ReleaseMode.Any);
        return true;
    }

    public Task<long> DeleteAllAsync()
    {
        return _collection.DeleteManyUnlockedAsync(x => true);
    }

    public IAsyncEnumerable<EntityLock<MyLockableEntity, ObjectId>> GetLockedAsync(LockMode mode)
    {
        return _collection.GetLockedAsync(mode);
    }
}
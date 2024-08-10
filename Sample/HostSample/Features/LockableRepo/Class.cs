using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Tharga.MongoDB;
using Tharga.MongoDB.Lockable;

namespace HostSample.Features.LockableRepo;

public record MyLoclableEntity : LockableEntityBase<ObjectId>
{
    public int Counter { get; set; }
}

public interface IMyLockableCollection : ILockableRepositoryCollection<MyLoclableEntity, ObjectId>
{
}

public interface IMyLockableRepo : IRepository
{
    IAsyncEnumerable<MyLoclableEntity> GetAll();
    //Task ResetAllCounters();
    //Task IncreaseAllCounters();
}

public class MyLockableCollection : LockableRepositoryCollectionBase<MyLoclableEntity, ObjectId>, IMyLockableCollection
{
    public MyLockableCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<MyLockableCollection> logger)
        : base(mongoDbServiceFactory, logger)
    {
    }
}

public class MyLockableRepo : IMyLockableRepo
{
    private readonly IMyLockableCollection _collection;

    public MyLockableRepo(IMyLockableCollection collection)
    {
        _collection = collection;
    }

    public IAsyncEnumerable<MyLoclableEntity> GetAll()
    {
        return _collection.GetAsync(x => true);
    }
}
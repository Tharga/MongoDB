using System.Collections.Generic;
using MongoDB.Bson;
using Tharga.MongoDB;

namespace ConsoleSample.SampleRepo;

public class MultiRepository : IMultiRepository
{
    private readonly ICollectionProvider _provider;

    public MultiRepository(ICollectionProvider provider)
    {
        _provider = provider;
    }

    public async IAsyncEnumerable<MyEntity> GetAll()
    {
        //var collectionA = _provider.GetDiskCollection<MyBaseEntity, ObjectId>("A", "a");
        //await collectionA.AddAsync(new MyEntity());
        //var allA = collectionA.GetAsync(x => true);
        //await foreach (var a in allA)
        //{
        //    yield return a as MyEntity;
        //}

        //var collectionB = _provider.GetBufferCollection<MyBaseEntity, ObjectId>("B", "b");
        //await collectionB.AddAsync(new MyEntity());
        //var allB = collectionB.GetAsync(x => true);
        //await foreach (var b in allB)
        //{
        //    yield return b as MyEntity;
        //}

        var collectionC = _provider.GetCollection<IMyRepo, MyBaseEntity, ObjectId>(new DatabaseContext { CollectionName = "C", DatabasePart = "c" });
        await collectionC.AddAsync(new MyEntity());
        var allC = collectionC.GetAsync(x => true);
        await foreach (var c in allC)
        {
            yield return c as MyEntity;
        }
    }
}
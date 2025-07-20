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
        var collectionC = _provider.GetCollection<IMyRepo, MyBaseEntity, ObjectId>(new DatabaseContext { CollectionName = "C", DatabasePart = "c" });
        await collectionC.AddAsync(new MyEntity());
        var allC = collectionC.GetAsync(x => true);
        await foreach (var c in allC)
        {
            yield return c as MyEntity;
        }
    }
}
using Tharga.MongoDB;

namespace HostSample.Features.DynamicRepo;

internal class DynRepo : IDynRepo
{
    private readonly ICollectionProvider _collectionProvider;

    public DynRepo(ICollectionProvider collectionProvider)
    {
        _collectionProvider = collectionProvider;
    }

    public IAsyncEnumerable<DynRepoItem> GetAsync(string instance)
    {
        var collection = GetCollection(instance);
        return collection.GetAsync();
    }

    public Task AddAsync(string instance, DynRepoItem item)
    {
        var collection = GetCollection(instance);
        return collection.AddAsync(item);
    }

    private IDynRepoCollection GetCollection(string instance)
    {
        return _collectionProvider.GetCollection<IDynRepoCollection, DynRepoItem>(new DatabaseContext
        {
            //DatabasePart = instance,
            CollectionName = $"Dyn_{instance}"
        });
    }
}
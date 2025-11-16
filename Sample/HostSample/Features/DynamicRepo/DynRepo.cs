using Tharga.MongoDB;

namespace HostSample.Features.DynamicRepo;

internal class DynRepo : IDynRepo
{
    private readonly ICollectionProvider _collectionProvider;

    public DynRepo(ICollectionProvider collectionProvider)
    {
        _collectionProvider = collectionProvider;
    }

    public IAsyncEnumerable<DynRepoItem> GetAsync(string configurationName, string databasePart, string instance)
    {
        var collection = GetCollection(configurationName, databasePart, instance);
        return collection.GetAsync();
    }

    public Task AddAsync(string configurationName, string databasePart, string instance, DynRepoItem item)
    {
        var collection = GetCollection(configurationName, databasePart, instance);
        return collection.AddAsync(item);
    }

    private IDynRepoCollection GetCollection(string configurationName, string databasePart, string instance)
    {
        return _collectionProvider.GetCollection<IDynRepoCollection, DynRepoItem>(new DatabaseContext
        {
            ConfigurationName = configurationName,
            DatabasePart = databasePart,
            CollectionName = $"Dyn_{instance}",
        });
    }
}
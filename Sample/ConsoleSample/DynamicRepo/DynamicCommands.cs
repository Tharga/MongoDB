using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tharga.Console.Commands.Base;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace ConsoleSample.DynamicRepo;

internal class DynamicCommands : ContainerCommandBase
{
    public DynamicCommands()
        : base("Dynamic")
    {
        RegisterCommand<DynamicAddCommand>();
        RegisterCommand<DynamicListCommand>();
    }
}

internal class DynamicAddCommand : AsyncActionCommandBase
{
    private readonly IDynRepository _dynRepository;

    public DynamicAddCommand(IDynRepository dynRepository)
        : base("Add")
    {
        _dynRepository = dynRepository;
    }

    public override async Task InvokeAsync(string[] param)
    {
        var instance = QueryParam<string>("Instance", param);

        await _dynRepository.AddAsync(instance, new DynEntity());
    }
}

internal class DynamicListCommand : AsyncActionCommandBase
{
    private readonly IDynRepository _dynRepository;

    public DynamicListCommand(IDynRepository dynRepository)
        : base("List")
    {
        _dynRepository = dynRepository;
    }

    public override async Task InvokeAsync(string[] param)
    {
        var instance = QueryParam<string>("Instance", param);

        var response = await _dynRepository.GetAsync(instance).ToArrayAsync();
        var title = new[] { "Id", "Created" };
        var data = response.Select(x => new[] { $"{x.Id}", x.Id.CreationTime.ToString("yyyy-MM-dd HH:mm:ss") });
        OutputTable(title, data);
    }
}

public interface IDynRepository : IRepository
{
    Task AddAsync(string instance, DynEntity dynEntity);
    IAsyncEnumerable<DynEntity> GetAsync(string instance);
}

internal class DynRepository : IDynRepository
{
    private readonly ICollectionProvider _collectionProvider;

    public DynRepository(ICollectionProvider collectionProvider)
    {
        _collectionProvider = collectionProvider;
    }

    public async Task AddAsync(string instance, DynEntity dynEntity)
    {
        var collection = GetCollection(instance);
        await collection.AddAsync(dynEntity);
    }

    public IAsyncEnumerable<DynEntity> GetAsync(string instance)
    {
        var collection = GetCollection(instance);
        return collection.GetAsync();
    }

    private IDynRepositoryCollection GetCollection(string instance)
    {
        var databaseContext = new DatabaseContext
        {
            CollectionName = $"Dyn_{instance}"
        };
        var collection = _collectionProvider.GetCollection<IDynRepositoryCollection, DynEntity>(databaseContext);
        return collection;
    }
}

internal class DynRepositoryCollection : DiskRepositoryCollectionBase<DynEntity>, IDynRepositoryCollection
{
    public DynRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<DynRepositoryCollection> logger, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }
}
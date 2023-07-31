using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HostSample.Entities;
using HostSample.Features.BasicDiskRepo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace HostSample.Features.Experimental;

public interface IExperimentalDiskRepo : IRepository
{
    IAsyncEnumerable<MyEntity> GetAll();
    Task ResetAllCounters();
    Task IncreaseAllCounters();
}

public interface IExperimentalDiskRepoCollection : IDiskRepositoryCollection<MyEntity, ObjectId>
{
}

public class ExperimentalDiskRepoCollection : Tharga.MongoDB.Experimental.ReadWriteDiskRepositoryCollectionBase<MyEntity, ObjectId>, IExperimentalDiskRepoCollection
{
    public ExperimentalDiskRepoCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<ExperimentalDiskRepoCollection> logger)
        : base(mongoDbServiceFactory, logger)
    {
    }

    //TODO: public override string DatabasePart => "MyDatabasePart";
    //TODO: public override string CollectionName => "MyCollection";
}

public class ExperimentalDiskRepo : IExperimentalDiskRepo
{
    private readonly IExperimentalDiskRepoCollection _collection;

    public ExperimentalDiskRepo(IExperimentalDiskRepoCollection collection)
    {
        _collection = collection;
    }

    public IAsyncEnumerable<MyEntity> GetAll()
    {
        return _collection.GetAsync(x => true);
    }

    public Task ResetAllCounters()
    {
        var filter = FilterDefinition<MyEntity>.Empty;
        var update = new UpdateDefinitionBuilder<MyEntity>().Set(x => x.Counter, 0);
        return _collection.UpdateAsync(filter, update);
    }

    public Task IncreaseAllCounters()
    {
        var filter = FilterDefinition<MyEntity>.Empty;
        var update = new UpdateDefinitionBuilder<MyEntity>().Inc(x => x.Counter, 1);
        return _collection.UpdateAsync(filter, update);
    }
}

[ApiController]
[Route("[controller]")]
public class ExperimentalDiskRepoController : ControllerBase
{
    private readonly IExperimentalDiskRepo _repository;

    public ExperimentalDiskRepoController(IExperimentalDiskRepo repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _repository.GetAll().ToArrayAsync();
        return Ok(items.Select(x => new { Id = x.Id.ToString(), x.Counter }));
    }

    [HttpPatch]
    [Route("counter/increase")]
    public async Task<IActionResult> IncreaseAllCounters()
    {
        await _repository.IncreaseAllCounters();
        return Accepted();
    }

    [HttpPatch]
    [Route("counter/reset")]
    public async Task<IActionResult> ResetAllCounters()
    {
        await _repository.ResetAllCounters();
        return Accepted();
    }
}
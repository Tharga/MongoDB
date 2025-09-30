using Microsoft.AspNetCore.Mvc;
using Tharga.MongoDB;

namespace HostSample.Controllers;

[ApiController]
[Route("[controller]")]
public class CollectionController : ControllerBase
{
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private readonly ICollectionTypeService _collectionTypeService;
    private readonly IDatabaseMonitor _databaseMonitor;

    public CollectionController(IMongoDbServiceFactory mongoDbServiceFactory, ICollectionTypeService collectionTypeService, IDatabaseMonitor databaseMonitor)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _collectionTypeService = collectionTypeService;
        _databaseMonitor = databaseMonitor;
    }

    [HttpGet]
    public Task<IActionResult> GetCollections()
    {
        var factory = _mongoDbServiceFactory.GetMongoDbService(() => new DatabaseContext());
        return Task.FromResult<IActionResult>(Ok(factory.GetCollections()));
    }

    [HttpGet("metadata")]
    public async Task<IActionResult> GetCollectionsWithMetaAsync()
    {
        var factory = _mongoDbServiceFactory.GetMongoDbService(() => new DatabaseContext());
        var collections = await factory.GetCollectionsWithMetaAsync().ToArrayAsync();
        return Ok(collections);
    }

    [HttpGet("collectionTypes")]
    public async Task<IActionResult> GetCollectionTypes()
    {
        var colTypes = _collectionTypeService.GetCollectionTypes();
        return Ok(colTypes.Select(x => new
        {
            ServiceType = x.ServiceType.Name,
            ImplementationType = x.ImplementationType.Name,
            x.IsDynamic
        }));
    }

    [HttpGet("monitor")]
    public async Task<IActionResult> GetMonitor()
    {
        var instances = await _databaseMonitor.GetInstancesAsync().ToArrayAsync();
        return Ok(instances);
    }

    //[HttpGet("index")]
    //public async Task<IActionResult> GetIndexes()
    //{
    //    var factory = _mongoDbServiceFactory.GetMongoDbService(() => new DatabaseContext());
    //    var collections = await factory.GetIndex().ToArrayAsync();
    //    return Ok(collections.Select(x => new { x.Name, x.IndexNames }));
    //}

    //[HttpGet("repo")]
    //public async Task<IActionResult> GetRepositories()
    //{
    //    var collection = _metadataService.GetRepositories().ToArray();
    //    return Ok(collection);
    //}
}
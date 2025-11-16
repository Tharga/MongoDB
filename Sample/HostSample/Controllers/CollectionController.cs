using HostSample.Features.DynamicRepo;
using HostSample.Features.SecondaryRepo;
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
    private readonly IDynRepo _dynRepo;
    private readonly ISecondaryRepo _secondaryRepo;

    public CollectionController(IMongoDbServiceFactory mongoDbServiceFactory, ICollectionTypeService collectionTypeService, IDatabaseMonitor databaseMonitor, IDynRepo dynRepo, ISecondaryRepo secondaryRepo)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _collectionTypeService = collectionTypeService;
        _databaseMonitor = databaseMonitor;
        _dynRepo = dynRepo;
        _secondaryRepo = secondaryRepo;
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
        //TODO: Use to touch all databases, so information can be loaded.
        //var t1 = await _dynRepo.GetAsync("NoDefault", "part", "A").ToArrayAsync();
        //var t11 = await _dynRepo.GetAsync("NoDefault", "part2", "A").ToArrayAsync();
        //var t2 = await _dynRepo.GetAsync("Secondary", "part", "A").ToArrayAsync();
        //var t3d = await _secondaryRepo.GetAsync().ToArrayAsync();

        var items = await _databaseMonitor.GetInstancesAsync().ToArrayAsync();
        //return Ok(items);
        return Ok(items.Select(x => new
        {
            Source = $"{x.Source}",
            x.ConfigurationName,
            x.Server,
            x.DatabaseName,
            x.CollectionName,
            x.CollectionTypeName,
            Registration = $"{x.Registration}",
            x.AccessCount,
            x.DocumentCount,
            x.Size,
            x.Types,
        }));
    }

    [HttpGet("configuration")]
    public Task<IActionResult> GetConfigurations()
    {
        var configurations = _databaseMonitor.GetConfigurations();
        return Task.FromResult<IActionResult>(Ok(configurations.Select(x => x.Value)));
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
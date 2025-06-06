using Microsoft.AspNetCore.Mvc;
using Tharga.MongoDB;

namespace HostSample.Controllers;

[ApiController]
[Route("[controller]")]
public class CollectionController : ControllerBase
{
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;

    public CollectionController(IMongoDbServiceFactory mongoDbServiceFactory)
    {
        _mongoDbServiceFactory = mongoDbServiceFactory;
    }

    [HttpGet]
    public async Task<IActionResult> GetCollections()
    {
        var factory = _mongoDbServiceFactory.GetMongoDbService(() => new DatabaseContext());
        return Ok(factory.GetCollections());
    }

    [HttpGet("metadata")]
    public async Task<IActionResult> GetCollectionsWithMetaAsync()
    {
        var factory = _mongoDbServiceFactory.GetMongoDbService(() => new DatabaseContext());
        var collections = await factory.GetCollectionsWithMetaAsync().ToArrayAsync();
        return Ok(collections.Select(x => new { x.Name, x.Size, x.DocumentCount }));
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
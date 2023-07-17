using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

namespace HostSample.Features.DynamicDiskRepo;

[ApiController]
[Route("[controller]")]
public class DynamicDiskRepoController : ControllerBase
{
    private readonly IMyDynamicDiskRepoRepo _repository;

    public DynamicDiskRepoController(IMyDynamicDiskRepoRepo repository)
    {
        _repository = repository;
    }

    [HttpGet]
    [Route("{key}")]
    public async Task<IActionResult> Get(string key, [FromQuery] string configurationName = "Default", [FromQuery] string collectionName = "MyCollection", [FromQuery] string databasePart = "MyDatabasePart")
    {
        var item = await _repository.GetOne(new ObjectId(key), configurationName, collectionName, databasePart);
        return Ok(item);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(string configurationName = "Default", string collectionName = "MyCollection", string databasePart = "MyDatabasePart")
    {
        var items = await _repository.GetAll(configurationName, collectionName, databasePart).ToArrayAsync();
        return Ok(items);
    }

    [HttpPost]
    [Route("CreateRandom")]
    public async Task<IActionResult> CreateRandom(string configurationName = "Default", string collectionName = "MyCollection", string databasePart = "MyDatabasePart")
    {
        var id = await _repository.CreateRandom(configurationName, collectionName, databasePart);
        return Created(new Uri($"{Request.Scheme}://{Request.Host}/DynamicDiskRepo/{id}?configurationName={configurationName}&collectionName={collectionName}&databasePart={databasePart}"), id);
    }

    [HttpPatch]
    public async Task<IActionResult> Count(string key, [FromQuery] string configurationName = "Default", [FromQuery] string collectionName = "MyCollection", [FromQuery] string databasePart = "MyDatabasePart")
    {
        var item = await _repository.Count(new ObjectId(key), configurationName, collectionName, databasePart);
        return Ok(item);
    }
}
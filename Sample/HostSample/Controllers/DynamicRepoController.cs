using HostSample.Features.DynamicRepo;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

namespace HostSample.Controllers;

[ApiController]
[Route("[controller]")]
public class DynamicRepoController : ControllerBase
{
    private readonly IDynRepo _dynRepo;

    public DynamicRepoController(IDynRepo dynRepo)
    {
        _dynRepo = dynRepo;
    }

    [HttpGet]
    public async Task<IActionResult> Get(string configurationName, string databasePart, string instance)
    {
        var items = await _dynRepo.GetAsync(configurationName, databasePart, instance).ToArrayAsync();
        return Ok(items.Select(x => new { Id = x.Id.ToString() }));
    }

    [HttpPost]
    public async Task<IActionResult> Add(string configurationName, string databasePart, string instance)
    {
        await _dynRepo.AddAsync(configurationName, databasePart, instance, new DynRepoItem { Id = ObjectId.GenerateNewId() });
        return Ok();
    }
}
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

namespace Tharga.MongoDB.HostSample.Features.SlimDiskRepo;

[ApiController]
[Route("[controller]")]
public class SlimDiskRepoController : ControllerBase
{
    private readonly MySlimDiskRepo _repository;

    public SlimDiskRepoController(MySlimDiskRepo repository)
    {
        _repository = repository;
    }

    [HttpGet]
    [Route("{key}")]
    public async Task<IActionResult> Get(string key)
    {
        var item = await _repository.GetAll().FirstOrDefaultAsync(x => x.Id == new ObjectId(key));
        return Ok(item);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _repository.GetAll().ToArrayAsync();
        return Ok(items.Select(x => x.Id.ToString()));
    }

    [HttpPost]
    [Route("CreateRandom")]
    public async Task<IActionResult> CreateRandom()
    {
        var id = await _repository.CreateRandom();
        return Created(new Uri($"{Request.Scheme}://{Request.Host}/SlimDiskRepo/{id}"), id);
    }

    [HttpDelete]
    [Route("{key}")]
    public async Task<IActionResult> Delete(string key)
    {
        var id = await _repository.DeleteOneAsync(x => x.Id == new ObjectId(key));
        return Ok();
    }
}
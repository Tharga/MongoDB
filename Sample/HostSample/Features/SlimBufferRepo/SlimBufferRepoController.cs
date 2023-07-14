using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

namespace HostSample.Features.SlimBufferRepo;

[ApiController]
[Route("[controller]")]
public class SlimBufferRepoController : ControllerBase
{
    private readonly MySlimBufferRepo _repository;

    public SlimBufferRepoController(MySlimBufferRepo repository)
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
        return Created(new Uri($"{Request.Scheme}://{Request.Host}/SlimBufferRepo/{id}"), id);
    }

    [HttpDelete]
    [Route("{key}")]
    public async Task<IActionResult> Delete(string key)
    {
        var id = await _repository.DeleteOneAsync(x => x.Id == new ObjectId(key));
        return Ok(id.ToString());
    }

    [HttpPost]
    [Route("Invalidate")]
    public async Task<IActionResult> InvalidateBuffer()
    {
        await _repository.InvalidateBufferAsync();
        return Ok();
    }
}
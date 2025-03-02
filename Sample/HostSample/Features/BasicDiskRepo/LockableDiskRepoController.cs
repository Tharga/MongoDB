using HostSample.Features.LockableRepo;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

namespace HostSample.Features.BasicDiskRepo;

[ApiController]
[Route("[controller]")]
public class LockableDiskRepoController : ControllerBase
{
    private readonly IMyLockableRepo _repository;

    public LockableDiskRepoController(IMyLockableRepo repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _repository.GetAll().ToArrayAsync();
        return Ok(items.Select(x => new { Id = x.Id.ToString(), x.Counter }));
    }

    [HttpPost]
    public async Task<IActionResult> Create()
    {
        var myLockableEntity = new MyLockableEntity { Counter = 0 };
        await _repository.AddAsync(myLockableEntity);
        return Ok(myLockableEntity);
    }

    [HttpGet("BumpCount/{id}")]
    public async Task<IActionResult> Count(string id)
    {
        var item = await _repository.BumpCountAsync(ObjectId.Parse(id));
        return Ok(item);
    }

    [HttpPut("Lock/{id}")]
    public async Task<IActionResult> Lock([FromRoute] string id)
    {
        await _repository.LockAsync(ObjectId.Parse(id), TimeSpan.FromSeconds(10), null);
        return Accepted();
    }
}
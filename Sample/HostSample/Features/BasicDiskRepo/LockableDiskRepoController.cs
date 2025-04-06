using HostSample.Features.LockableRepo;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using Tharga.MongoDB.Lockable;

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

    [HttpGet("unlocked")]
    public async Task<IActionResult> GetUnlocked()
    {
        var items = await _repository.GetUnlocked().ToArrayAsync();
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

    [HttpPost("throw/{id}")]
    public async Task<IActionResult> Throw(string id)
    {
        await _repository.ThrowAsync(ObjectId.Parse(id));
        return Ok();
    }

    [HttpPost("unlock/{id}")]
    public async Task<IActionResult> Unlock(string id)
    {
        var result = await _repository.UnlockAsync(ObjectId.Parse(id));
        return Ok(result);
    }

    [HttpPut("Lock/{id}/{lockTimeSeconds}")]
    public async Task<IActionResult> Lock([FromRoute] string id, int lockTimeSeconds = 10)
    {
        await _repository.LockAsync(ObjectId.Parse(id), TimeSpan.FromSeconds(lockTimeSeconds), null);
        return Accepted();
    }

    [HttpDelete]
    public async Task<IActionResult> Lock()
    {
        var count = await _repository.DeleteAllAsync();
        return Ok($"Deleted {count} items.");
    }

    /// <summary>
    /// Get locked items.
    /// </summary>
    /// <param name="mode">0 = Locked, 1 = Expired, 2 = Exception</param>
    /// <returns></returns>
    [HttpGet("locked")]
    public async Task<IActionResult> GetLocked(LockMode mode)
    {
        var items = await _repository.GetLockedAsync(mode).ToArrayAsync();
        return Ok(items);
    }
}
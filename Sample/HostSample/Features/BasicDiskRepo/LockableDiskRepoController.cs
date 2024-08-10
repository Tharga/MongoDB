using System.Linq;
using System.Threading.Tasks;
using HostSample.Features.LockableRepo;
using Microsoft.AspNetCore.Mvc;
using Tharga.MongoDB;

namespace HostSample.Features.BasicDiskRepo;

[ApiController]
[Route("[controller]")]
public class LockableDiskRepoController : ControllerBase
{
    private readonly IMyLockableRepo _repository;

    public LockableDiskRepoController(IMyLockableRepo repository, IRepositoryConfiguration configuration)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _repository.GetAll().ToArrayAsync();
        return Ok(items.Select(x => new { Id = x.Id.ToString(), x.Counter }));
    }
}
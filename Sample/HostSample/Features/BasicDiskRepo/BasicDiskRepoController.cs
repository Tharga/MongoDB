using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace HostSample.Features.BasicDiskRepo;

[ApiController]
[Route("[controller]")]
public class BasicDiskRepoController : ControllerBase
{
    private readonly IMyBasicDiskRepo _repository;

    public BasicDiskRepoController(IMyBasicDiskRepo repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _repository.GetAll().ToArrayAsync();
        return Ok(items.Select(x => x.Id.ToString()));
    }
}
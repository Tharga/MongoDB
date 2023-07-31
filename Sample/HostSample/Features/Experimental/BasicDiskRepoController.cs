using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace HostSample.Features.Experimental;

[ApiController]
[Route("[controller]")]
public class ExperimentalDiskRepoController : ControllerBase
{
    private readonly IExperimentalDiskRepo _repository;

    public ExperimentalDiskRepoController(IExperimentalDiskRepo repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _repository.GetAll().ToArrayAsync();
        return Ok(items.Select(x => new { Id = x.Id.ToString(), x.Counter }));
    }

    [HttpPatch]
    [Route("counter/increase")]
    public async Task<IActionResult> IncreaseAllCounters()
    {
        await _repository.IncreaseAllCounters();
        return Accepted();
    }

    [HttpPatch]
    [Route("counter/reset")]
    public async Task<IActionResult> ResetAllCounters()
    {
        await _repository.ResetAllCounters();
        return Accepted();
    }
}
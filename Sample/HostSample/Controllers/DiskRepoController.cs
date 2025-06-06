using HostSample.Features.DiskRepo;
using Microsoft.AspNetCore.Mvc;

namespace HostSample.Controllers;

[ApiController]
[Route("[controller]")]
public class DiskRepoController : ControllerBase
{
    private static readonly string[] _summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    private readonly IWeatherForecastRepository _weatherForecastRepository;
    private readonly ILogger<DiskRepoController> _logger;

    public DiskRepoController(IWeatherForecastRepository weatherForecastRepository, ILogger<DiskRepoController> logger)
    {
        _weatherForecastRepository = weatherForecastRepository;
        _logger = logger;
    }

    [HttpGet("Random")]
    public async Task<IActionResult> GetRandom()
    {
        var result = Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = _summaries[Random.Shared.Next(_summaries.Length)],
                SomeGuid = Guid.NewGuid()
            })
            .ToArray();

        await _weatherForecastRepository.AddRangeAsync(result);

        return Ok(result);
    }

    [HttpGet("Database")]
    public async Task<IActionResult> GetFromDatabase()
    {
        var result = await _weatherForecastRepository.GetAsync().ToArrayAsync();

        return Ok(result);
    }
}
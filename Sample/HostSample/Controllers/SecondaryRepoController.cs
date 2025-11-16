using HostSample.Features.DiskRepo;
using HostSample.Features.SecondaryRepo;
using Microsoft.AspNetCore.Mvc;
using System;
using MongoDB.Bson;

namespace HostSample.Controllers;

[ApiController]
[Route("[controller]")]
public class SecondaryRepoController : ControllerBase
{
    private static readonly string[] _summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    private readonly ISecondaryRepo _secondaryRepo;

    public SecondaryRepoController(ISecondaryRepo secondaryRepo)
    {
        _secondaryRepo = secondaryRepo;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var items = await _secondaryRepo.GetAsync().ToArrayAsync();
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Set()
    {
        var entity = new WeatherForecast
        {
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            SomeGuid = Guid.NewGuid(),
            Summary = _summaries[Random.Shared.Next(_summaries.Length)],
            TemperatureC = Random.Shared.Next(-20, 55),
            Id = ObjectId.GenerateNewId(),
        };
        await _secondaryRepo.SetAsync(entity);
        return Accepted(entity);
    }
}
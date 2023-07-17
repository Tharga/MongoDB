using System;
using System.Linq;
using System.Threading.Tasks;
using HostSample.Entities;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

namespace HostSample.Features.MultiTypeDiskRepo;

[ApiController]
[Route("[controller]")]
public class MultiTypeDiskRepoController : ControllerBase
{
    private readonly IMyMultiTypeDiskRepo _repository;

    public MultiTypeDiskRepoController(IMyMultiTypeDiskRepo repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _repository.GetAll().ToArrayAsync();
        return Ok(items.Select(BuildResult));
    }

    [HttpGet]
    [Route("type")]
    public async Task<IActionResult> GetAllByType(EntityType entityType = EntityType.First)
    {
        object items;
        switch (entityType)
        {
            case EntityType.First:
                items = (await _repository.GetByType<MyFirstEntity>().ToArrayAsync()).Select(BuildResult);
                break;
            case EntityType.Second:
                items = (await _repository.GetByType<MySecondEntity>().ToArrayAsync()).Select(BuildResult);
                break;
            default:
                throw new ArgumentOutOfRangeException($"Unknown type '{entityType}'.");
        }

        return Ok(items);
    }

    [HttpPost]
    [Route("CreateRandom")]
    public async Task<IActionResult> CreateRandom(EntityType entityType = EntityType.First)
    {
        ObjectId id;
        switch (entityType)
        {
            case EntityType.First:
                var myFirstEntity = new MyFirstEntity();
                await _repository.CreateRandom(myFirstEntity);
                id = myFirstEntity.Id;
                break;
            case EntityType.Second:
                var mySecondEntity = new MySecondEntity();
                await _repository.CreateRandom(mySecondEntity);
                id = mySecondEntity.Id;
                break;
            default:
                throw new ArgumentOutOfRangeException($"Unknown type '{entityType}'.");
        }

        return Created(new Uri($"{Request.Scheme}://{Request.Host}/{nameof(MultiTypeDiskRepoController).Replace("Controller", "")}/{id}"), id);
    }

    private static object BuildResult<T>(T x) where T : MyEntityBase
    {
        return new { Id = x.Id.ToString(), Type = x.GetType().ToString() };
    }

    public enum EntityType
    {
        First,
        Second
    }
}
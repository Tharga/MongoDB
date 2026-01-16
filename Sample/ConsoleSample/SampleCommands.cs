using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConsoleSample.SampleRepo;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Tharga.Console.Commands.Base;

namespace ConsoleSample;

internal class SampleCommands : ContainerCommandBase
{
    public SampleCommands()
        : base("Sample")
    {
        RegisterCommand<AddCommand>();
        RegisterCommand<CountCommand>();
        RegisterCommand<ListCommand>();
        RegisterCommand<BurstCommand>();
    }
}

internal class BurstCommand : AsyncActionCommandBase
{
    private readonly ISampleRepository _sampleRepository;
    private readonly ILogger<BurstCommand> _logger;

    public BurstCommand(ISampleRepository sampleRepository, ILogger<BurstCommand> logger)
        : base("Burst")
    {
        _sampleRepository = sampleRepository;
        _logger = logger;
    }

    public override async Task InvokeAsync(string[] param)
    {
        var count = QueryParam<int>("Count", param);

        var tasks = new List<Task>();
        var cnt = 0;
        for (var i = 0; i < count; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await _sampleRepository.AddAsync(new SampleEntity { Id = ObjectId.GenerateNewId() });
                _ = await _sampleRepository.GetAsync().ToArrayAsync();
                _logger.LogTrace("Step {step}", ++cnt);
            }));
        }

        await Task.WhenAll(tasks);
        //OutputInformation("Complete.");
        _logger.LogInformation("Complete.");
    }
}

internal class CountCommand : AsyncActionCommandBase
{
    private readonly ISampleRepository _sampleRepository;

    public CountCommand(ISampleRepository sampleRepository)
        : base("Count")
    {
        _sampleRepository = sampleRepository;
    }

    public override async Task InvokeAsync(string[] param)
    {
        var response = await _sampleRepository.CountAsync();
        OutputInformation($"Count = {response}");
    }
}

internal class ListCommand : AsyncActionCommandBase
{
    private readonly ISampleRepository _sampleRepository;

    public ListCommand(ISampleRepository sampleRepository)
        : base("List")
    {
        _sampleRepository = sampleRepository;
    }

    public override async Task InvokeAsync(string[] param)
    {
        var response = await _sampleRepository.GetAsync().ToArrayAsync();
        var title = new[] { "Id", "Created" };
        var data = response.Select(x => new[] { $"{x.Id}", x.Id.CreationTime.ToString("yyyy-MM-dd HH:mm:ss") });
        OutputTable(title, data);
    }
}

internal class AddCommand : AsyncActionCommandBase
{
    private readonly ISampleRepository _sampleRepository;

    public AddCommand(ISampleRepository sampleRepository)
        : base("Add")
    {
        _sampleRepository = sampleRepository;
    }

    public override async Task InvokeAsync(string[] param)
    {
        await _sampleRepository.AddAsync(new SampleEntity());
    }
}
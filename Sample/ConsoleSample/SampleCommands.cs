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
        RegisterCommand<MonitorInfoCommand>();
    }
}

internal class MonitorInfoCommand : AsyncActionCommandBase
{
    private readonly Tharga.MongoDB.IDatabaseMonitor _monitor;

    public MonitorInfoCommand(Tharga.MongoDB.IDatabaseMonitor monitor)
        : base("Monitor")
    {
        _monitor = monitor;
    }

    public override async Task InvokeAsync(string[] param)
    {
        OutputInformation("=== Collections ===");
        await foreach (var info in _monitor.GetInstancesAsync())
        {
            OutputInformation($"  {info.ConfigurationName}.{info.DatabaseName}.{info.CollectionName}");
            OutputInformation($"    Registration: {info.Registration}, Discovery: {info.Discovery}");
            OutputInformation($"    CollectionType: {info.CollectionType?.Name ?? "(null)"}");
            OutputInformation($"    Stats: {(info.Stats != null ? $"Docs={info.Stats.DocumentCount:N0}, Size={info.Stats.Size:N0}" : "(null)")}");
            OutputInformation($"    Index: {(info.Index?.Current != null ? $"{info.Index.Current.Length} current" : "(null)")} / {(info.Index?.Defined != null ? $"{info.Index.Defined.Length} defined" : "(null)")}");
            OutputInformation($"    Clean: {(info.Clean != null ? $"Cleaned={info.Clean.DocumentsCleaned} at {info.Clean.CleanedAt}" : "(null)")}");
        }

        OutputInformation("");
        OutputInformation("=== Calls ===");
        var calls = _monitor.GetCalls(Tharga.MongoDB.CallType.Last).Take(5);
        foreach (var call in calls)
        {
            OutputInformation($"  {call.FunctionName} on {call.Fingerprint.CollectionName} — {call.Elapsed?.TotalMilliseconds:N2}ms");
        }
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
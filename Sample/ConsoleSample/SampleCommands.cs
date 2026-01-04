using System;
using System.Linq;
using System.Threading.Tasks;
using ConsoleSample.SampleRepo;
using Tharga.Console.Commands.Base;

namespace ConsoleSample;

internal class SampleCommands : ContainerCommandBase
{
    public SampleCommands()
        : base("Sample")
    {
        RegisterCommand<AddCommand>();
        RegisterCommand<ListCommand>();
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
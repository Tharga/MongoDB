using Tharga.MongoDB;

namespace ConsoleSample.SampleRepo;

public record SampleEntity : EntityBase
{
    public string Name { get; init; }
}
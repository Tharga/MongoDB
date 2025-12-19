using Tharga.MongoDB;

namespace HostSample.Features.DynamicRepo;

public record DynRepoItem : EntityBase
{
    public string Something { get; set; }
    public string Else { get; set; }
}
using Tharga.MongoDB;

namespace HostSample.Features.DynamicRepo;

public interface IDynRepo : IRepository
{
    IAsyncEnumerable<DynRepoItem> GetAsync(string instance);
    Task AddAsync(string instance, DynRepoItem item);
}
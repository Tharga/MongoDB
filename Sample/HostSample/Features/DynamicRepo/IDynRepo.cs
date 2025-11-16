using Tharga.MongoDB;

namespace HostSample.Features.DynamicRepo;

public interface IDynRepo : IRepository
{
    IAsyncEnumerable<DynRepoItem> GetAsync(string configurationName, string databasePart, string instance);
    Task AddAsync(string configurationName, string databasePart, string instance, DynRepoItem item);
}
using System.Collections.Generic;
using System.Threading.Tasks;
using Tharga.MongoDB;

namespace ConsoleSample.SampleRepo;

public interface ISampleRepository : IRepository
{
    IAsyncEnumerable<SampleEntity> GetAsync();
    Task AddAsync(SampleEntity entity);
    Task<long> CountAsync();
}
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ConsoleSample.SampleRepo;

internal class SampleRepository : ISampleRepository
{
    private readonly ISampleRepositoryCollection _collection;

    public SampleRepository(ISampleRepositoryCollection collection)
    {
        _collection = collection;
    }

    public IAsyncEnumerable<SampleEntity> GetAsync()
    {
        return _collection.GetAsync();
    }

    public Task AddAsync(SampleEntity entity)
    {
        return _collection.AddAsync(entity);
    }
}
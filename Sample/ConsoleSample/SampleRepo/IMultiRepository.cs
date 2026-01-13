using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace ConsoleSample.SampleRepo;

public interface IMultiRepository : IRepository
{
    IAsyncEnumerable<MyEntity> GetAll();
}

public interface ISampleRepository : IRepository
{
    IAsyncEnumerable<SampleEntity> GetAsync();
    Task AddAsync(SampleEntity entity);
}

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

public record SampleEntity : EntityBase
{
}

public interface ISampleRepositoryCollection : IDiskRepositoryCollection<SampleEntity>
{
}

internal class SampleRepositoryCollection : DiskRepositoryCollectionBase<SampleEntity>, ISampleRepositoryCollection
{
    public SampleRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<SampleRepositoryCollection> logger)
        : base(mongoDbServiceFactory, logger)
    {
    }
}
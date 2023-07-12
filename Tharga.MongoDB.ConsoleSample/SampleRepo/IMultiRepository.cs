using System.Collections.Generic;

namespace Tharga.MongoDB.ConsoleSample.SampleRepo;

public interface IMultiRepository : IRepository
{
    IAsyncEnumerable<MyEntity> GetAll();
}
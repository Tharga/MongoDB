using System.Collections.Generic;
using Tharga.MongoDB;

namespace ConsoleSample.SampleRepo;

public interface IMultiRepository : IRepository
{
    IAsyncEnumerable<MyEntity> GetAll();
}
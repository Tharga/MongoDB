using System.Collections.Generic;
using System.Threading.Tasks;
using HostSample.Entities;
using Tharga.MongoDB;

namespace HostSample.Features.Experimental;

public interface IExperimentalDiskRepo : IRepository
{
    IAsyncEnumerable<MyEntity> GetAll();
    Task ResetAllCounters();
    Task IncreaseAllCounters();
}
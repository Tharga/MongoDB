using System.Collections.Generic;
using System.Threading.Tasks;
using HostSample.Entities;
using Tharga.MongoDB;

namespace HostSample.Features.BasicDiskRepo;

public interface IMyBasicDiskRepo : IRepository
{
    IAsyncEnumerable<MyEntity> GetAll();
    Task ResetAllCounters();
    Task IncreaseAllCounters();
}
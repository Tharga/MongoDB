using System.Collections.Generic;
using Tharga.MongoDB;

namespace HostSample.Features.LockableRepo;

public interface IMyLockableRepo : IRepository
{
    IAsyncEnumerable<MyLockableEntity> GetAll();
    //Task ResetAllCounters();
    //Task IncreaseAllCounters();
}
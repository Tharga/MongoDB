using System.Collections.Generic;
using Tharga.MongoDB.HostSample.Entities;

namespace Tharga.MongoDB.HostSample.Features.BasicDiskRepo;

public interface IMyBasicDiskRepo : IRepository
{
    IAsyncEnumerable<MyEntity> GetAll();
}
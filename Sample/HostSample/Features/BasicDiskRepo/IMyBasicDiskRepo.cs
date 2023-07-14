using System.Collections.Generic;
using HostSample.Entities;
using Tharga.MongoDB;

namespace HostSample.Features.BasicDiskRepo;

public interface IMyBasicDiskRepo : IRepository
{
    IAsyncEnumerable<MyEntity> GetAll();
}
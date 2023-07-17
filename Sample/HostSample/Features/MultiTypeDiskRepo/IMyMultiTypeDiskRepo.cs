using System.Collections.Generic;
using System.Threading.Tasks;
using HostSample.Entities;
using Tharga.MongoDB;

namespace HostSample.Features.MultiTypeDiskRepo;

public interface IMyMultiTypeDiskRepo : IRepository
{
    IAsyncEnumerable<MyEntityBase> GetAll();
    IAsyncEnumerable<T> GetByType<T>() where T : MyEntityBase;
    Task CreateRandom<T>(T item) where T : MyEntityBase;
}
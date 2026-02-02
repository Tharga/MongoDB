using Tharga.MongoDB;

namespace ConsoleSample.DynamicRepo;

public interface IDynRepositoryCollection : IDiskRepositoryCollection<DynEntity>
{
}
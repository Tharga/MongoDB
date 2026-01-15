using System.Threading.Tasks;

namespace Tharga.MongoDB;

public interface IRepositoryCollection : IReadOnlyRepositoryCollection
{
    Task DropCollectionAsync();
}

public interface IRepositoryCollection<TEntity, TKey> : IReadOnlyRepositoryCollection<TEntity, TKey>, IRepositoryCollection
    where TEntity : EntityBase<TKey>
{
}
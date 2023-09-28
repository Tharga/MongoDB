using System.Threading.Tasks;
using MongoDB.Driver;

namespace Tharga.MongoDB;

public interface IDiskRepositoryCollection<TEntity, TKey> : IRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    Task<long> UpdateAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update);
}
using System;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Tharga.MongoDB;

public interface IRepositoryCollection : IReadOnlyRepositoryCollection
{
    Task DropCollectionAsync();
}

public interface IRepositoryCollection<TEntity, TKey> : IReadOnlyRepositoryCollection<TEntity, TKey>, IRepositoryCollection
    where TEntity : EntityBase<TKey>
{
    //Create
    Task AddAsync(TEntity entity);

    //Update

    //Delete
    Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate);
}
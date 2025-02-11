namespace Tharga.MongoDB;

public interface IDiskRepositoryCollection<TEntity, TKey> : IRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>;
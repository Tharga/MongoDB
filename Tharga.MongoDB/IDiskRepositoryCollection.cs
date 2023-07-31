//using System.Threading.Tasks;
//using MongoDB.Driver;

//namespace Tharga.MongoDB;

////TODO: Move all disk-specific operations that is not supported for buffer here, if this is a good approach
//public interface IDiskRepositoryCollection<TEntity, TKey> : IRepositoryCollection<TEntity, TKey>
//    where TEntity : EntityBase<TKey>
//{
//    Task<long> UpdateAsync(FilterDefinition<TEntity> filter, UpdateDefinition<TEntity> update);
//}
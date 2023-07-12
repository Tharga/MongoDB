using System.Collections.Concurrent;

namespace Tharga.MongoDB.Buffer;

internal interface IBufferCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    ConcurrentDictionary<TKey, TEntity> Data { get; }
    void Set(ConcurrentDictionary<TKey, TEntity> data);
}
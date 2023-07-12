using System.Collections.Concurrent;

namespace Tharga.MongoDB.Buffer;

internal class BufferCollection<TEntity, TKey> : IBufferCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    public ConcurrentDictionary<TKey, TEntity> Data { get; private set; }

    public void Set(ConcurrentDictionary<TKey, TEntity> data)
    {
        Data = data;
    }
}
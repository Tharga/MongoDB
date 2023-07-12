using System.Collections.Concurrent;

namespace Tharga.MongoDB.Buffer;

internal static class BufferLibrary
{
    private static readonly ConcurrentDictionary<string, object> BufferCollections = new();

    public static IBufferCollection<TEntity, TKey> GetBufferCollection<TEntity, TKey>()
        where TEntity : EntityBase<TKey>
    {
        var key = typeof(TEntity).Name;
        if (!BufferCollections.TryGetValue(key, out var bufferCollection))
        {
            bufferCollection = new BufferCollection<TEntity, TKey>();
            BufferCollections.TryAdd(key, bufferCollection);
        }

        return (IBufferCollection<TEntity, TKey>)bufferCollection;
    }
}
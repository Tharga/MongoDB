using System.Collections.Concurrent;

namespace Tharga.MongoDB.Buffer;

internal static class BufferLibrary
{
    private static readonly ConcurrentDictionary<string, object> _bufferCollections = new();

    public static IBufferCollection<TEntity, TKey> GetBufferCollection<TEntity, TKey>(DatabaseContext databaseContext)
        where TEntity : EntityBase<TKey>
    {
        var key = $"{typeof(TEntity).Name}.{databaseContext?.CollectionName}.{databaseContext?.DatabasePart}.{databaseContext?.ConfigurationName?.Value}";
        if (!_bufferCollections.TryGetValue(key, out var bufferCollection))
        {
            bufferCollection = new BufferCollection<TEntity, TKey>();
            _bufferCollections.TryAdd(key, bufferCollection);
        }

        return (IBufferCollection<TEntity, TKey>)bufferCollection;
    }
}
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Tharga.MongoDB;

/// <summary>
/// Per-process cache of <see cref="CollectionInfo"/> entries used by the admin
/// UI to render large collection lists synchronously while a background
/// revalidation refreshes stale rows. First load per process pays the full
/// cost; subsequent navigations across all admin users on the same host
/// render instantly from this cache.
/// </summary>
public sealed class CollectionInfoCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public sealed record Entry(CollectionInfo Info, DateTime RefreshedAt);

    public bool IsEmpty => _entries.IsEmpty;

    public IReadOnlyCollection<CollectionInfo> GetAll()
    {
        return _entries.Values.Select(x => x.Info).ToArray();
    }

    public Entry Upsert(CollectionInfo info)
    {
        var entry = new Entry(info, DateTime.UtcNow);
        _entries[info.Key] = entry;
        return entry;
    }

    public bool TryGet(string key, out Entry entry)
    {
        return _entries.TryGetValue(key, out entry);
    }

    public void Remove(string key)
    {
        _entries.TryRemove(key, out _);
    }

    public void Clear()
    {
        _entries.Clear();
    }
}

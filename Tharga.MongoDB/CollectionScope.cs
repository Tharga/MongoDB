using System;
using System.Diagnostics;
using MongoDB.Driver;

namespace Tharga.MongoDB;

public record CollectionScope<TEntity> : IDisposable
{
    private readonly Action<TimeSpan, Exception> _release;
    private readonly Stopwatch _stopwatch = new ();

    internal CollectionScope(IMongoCollection<TEntity> collection, Action<TimeSpan, Exception> release)
    {
        Collection = collection;
        _release = release;
        _stopwatch.Start();
    }

    public IMongoCollection<TEntity> Collection { get; }

    public void Dispose()
    {
        _stopwatch.Stop();
        _release.Invoke(_stopwatch.Elapsed, null);
    }
}
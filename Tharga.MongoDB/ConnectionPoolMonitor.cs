using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Tharga.MongoDB;

internal sealed class ConnectionPoolMonitor : IConnectionPoolMonitor
{
    private readonly ConcurrentDictionary<string, Counters> _pools = new();

    public void OnConnectionCreated(string serverKey)
    {
        var c = _pools.GetOrAdd(serverKey, _ => new Counters());
        Interlocked.Increment(ref c.Open);
    }

    public void OnConnectionClosed(string serverKey)
    {
        var c = _pools.GetOrAdd(serverKey, _ => new Counters());
        // Guard against a close observed before its create (event ordering) going negative.
        if (Interlocked.Decrement(ref c.Open) < 0)
            Interlocked.Exchange(ref c.Open, 0);
    }

    public void SetMaxPoolSize(string serverKey, int maxPoolSize)
    {
        var c = _pools.GetOrAdd(serverKey, _ => new Counters());
        Interlocked.Exchange(ref c.MaxPoolSize, maxPoolSize);
    }

    public IReadOnlyList<ConnectionPoolCount> GetSnapshot()
    {
        var list = new List<ConnectionPoolCount>(_pools.Count);
        foreach (var (serverKey, c) in _pools)
        {
            list.Add(new ConnectionPoolCount
            {
                ServerKey = serverKey,
                OpenConnections = Volatile.Read(ref c.Open),
                MaxPoolSize = Volatile.Read(ref c.MaxPoolSize),
            });
        }
        return list;
    }

    private sealed class Counters
    {
        public int Open;
        public int MaxPoolSize;
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Tharga.MongoDB.Internals;

/// <summary>
/// Per-source ring buffer of recent monitor messages. In-memory and bounded; purely diagnostic.
/// </summary>
internal sealed class CommunicationLog : ICommunicationLog
{
    private const int MaxPerSource = 200;
    private readonly ConcurrentDictionary<string, Ring> _bySource = new();

    public void Record(string sourceName, CommunicationDirection direction, string messageType, string summary)
    {
        if (string.IsNullOrEmpty(sourceName)) return;
        var ring = _bySource.GetOrAdd(sourceName, _ => new Ring(MaxPerSource));
        ring.Add(new CommunicationEvent
        {
            Timestamp = DateTime.UtcNow,
            Direction = direction,
            MessageType = messageType,
            Summary = summary,
        });
    }

    public IReadOnlyList<CommunicationEvent> Get(string sourceName)
    {
        if (string.IsNullOrEmpty(sourceName) || !_bySource.TryGetValue(sourceName, out var ring))
            return [];
        return ring.Snapshot();
    }

    private sealed class Ring
    {
        private readonly CommunicationEvent[] _items;
        private readonly object _lock = new();
        private int _head;
        private int _count;

        public Ring(int size) => _items = new CommunicationEvent[size];

        public void Add(CommunicationEvent e)
        {
            lock (_lock)
            {
                _items[_head] = e;
                _head = (_head + 1) % _items.Length;
                if (_count < _items.Length) _count++;
            }
        }

        public IReadOnlyList<CommunicationEvent> Snapshot()
        {
            lock (_lock)
            {
                var result = new List<CommunicationEvent>(_count);
                for (var i = 0; i < _count; i++)
                {
                    var idx = (_head - 1 - i + _items.Length) % _items.Length;
                    result.Add(_items[idx]);
                }
                return result; // newest first
            }
        }
    }
}

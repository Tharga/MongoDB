using System;

namespace Tharga.MongoDB.Internals;

internal record CollectionAccessData
{
    public required Type EntityType { get; init; }
    public required Type CollectionType { get; init; }
    public DateTime FirstAccessed { get; internal set; }
    public DateTime LastAccessed { get; internal set; }
    public int AccessCount { get; internal set; }
}
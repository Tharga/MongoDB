using System;

namespace Tharga.MongoDB.Internals;

internal record CollectionAccessData : CollectionFingerprint
{
    public required string Server { get; internal set; }
    public required string DatabasePart { get; internal init; }
    public DateTime FirstAccessed { get; internal set; }
    public DateTime LastAccessed { get; internal set; }
    public int AccessCount { get; internal set; }
    public int CallCount { get; internal set; }
    public Type[] EntityTypes { get; internal set; }
}
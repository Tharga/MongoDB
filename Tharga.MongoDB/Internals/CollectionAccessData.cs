using System;

namespace Tharga.MongoDB.Internals;

internal record CollectionAccessData
{
    public required DatabaseContext DatabaseContext { get; internal init; }
    //public required string ConfigurationName { get; internal init; }
    //public required string CollectionName { get; internal init; }
    public required Type EntityType { get; internal init; }
    public required Type CollectionType { get; internal init; }
    public DateTime FirstAccessed { get; internal set; }
    public DateTime LastAccessed { get; internal set; }
    public int AccessCount { get; internal set; }
    public string Server { get; internal set; }
    //public string Database { get; internal set; }
    //public string DatabasePart { get; internal set; }
}
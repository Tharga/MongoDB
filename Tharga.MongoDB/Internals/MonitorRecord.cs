namespace Tharga.MongoDB.Internals;

internal record MonitorRecord
{
    public string CollectionName { get; init; }   // = _id in MongoDB
    public string ConfigurationName { get; init; }
    public string DatabaseName { get; init; }
    public string Server { get; init; }
    public string DatabasePart { get; init; }
    public int Source { get; init; }
    public int Registration { get; init; }
    public string[] Types { get; init; }
    public string CollectionTypeName { get; init; }
    public long DocumentCount { get; init; }
    public long Size { get; init; }
    public int AccessCount { get; init; }
    public int CallCount { get; init; }
    public IndexMeta[] CurrentIndexes { get; init; }
}

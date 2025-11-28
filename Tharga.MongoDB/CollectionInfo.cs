using System;

namespace Tharga.MongoDB;

public record CallInfo
{
    public required DateTime StartTime { get; init; }
    public required string CollectionName { get; init; }
    public required string FunctionName { get; init; }
    public TimeSpan? Elapsed { get; set; }
    public Exception Exception { get; set; }
}

public record CollectionInfo
{
    public required string ConfigurationName { get; init; }
    public required string Server { get; init; }
    public required string DatabaseName { get; init; }
    public required string CollectionName { get; init; }
    public DatabaseContext Context { get; init; }
    public Uri Uri => new(new Uri(Server), $"{DatabaseName}?collection={CollectionName}");
    public required Source Source { get; init; }
    public Registration Registration { get; init; }
    public required DocumentCount DocumentCount { get; init; }

    //--> Revisit

    public string CollectionTypeName { get; init; }
    public int AccessCount { get; init; }
    public required long Size { get; init; }
    public required string[] Types { get; init; }
    public required IndexInfo Index { get; init; }
}
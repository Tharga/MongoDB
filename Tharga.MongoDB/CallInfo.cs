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
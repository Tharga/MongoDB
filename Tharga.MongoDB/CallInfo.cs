using System;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB;

public record CallInfo
{
    public required DateTime StartTime { get; init; }
    public required string CollectionName { get; init; }
    public required string FunctionName { get; init; }
    public required Operation Operation { get; set; }
    public TimeSpan? Elapsed { get; set; }
    public int? Count { get; set; }
    public Exception Exception { get; set; }
    public bool Final { get; set; }
}
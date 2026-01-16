using System;

namespace Tharga.MongoDB;

internal record ExecuteInfo
{
    public required TimeSpan QueueElapsed { get; init; }
    public required int ConcurrentCount { get; init; }
    public required int QueueCount { get; init; }
}
using System;

namespace Tharga.MongoDB;

public record CallStepInfo
{
    public required string Step { get; init; }
    public required TimeSpan Delta { get; init; }
    public string Message { get; init; }
}

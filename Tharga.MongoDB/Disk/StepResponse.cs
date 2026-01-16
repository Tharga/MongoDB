namespace Tharga.MongoDB.Disk;

internal record StepResponse
{
    public required long Timestamp { get; init; }
    public required string Step { get; init; }
    public string Message { get; init; }
}

internal record StepResponse<T> : StepResponse
{
    public required T Value { get; init; }
}
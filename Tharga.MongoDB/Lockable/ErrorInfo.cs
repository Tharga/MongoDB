namespace Tharga.MongoDB.Lockable;

internal record ErrorInfo
{
    public required string Message { get; init; }
    public required ErrorInfoType Type { get; init; }
}
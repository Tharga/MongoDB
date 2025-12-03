namespace Tharga.MongoDB;

internal record DatabaseContextWithFingerprint : DatabaseContext
{
    public required string DatabaseName { get; init; }
}
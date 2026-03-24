using System;

namespace Tharga.MongoDB;

public record CleanInfo
{
    public required string SchemaFingerprint { get; init; }
    public required DateTime CleanedAt { get; init; }
    public required int DocumentsCleaned { get; init; }
}

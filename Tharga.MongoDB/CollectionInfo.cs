using System;

namespace Tharga.MongoDB;

public record CollectionInfo : CollectionFingerprint
{
    public required string Server { get; init; }
    public string DatabasePart { get; init; }
    public Source Source { get; set; }
    public required Registration Registration { get; init; }
    public required string[] EntityTypes { get; init; }
    public required Type CollectionType { get; init; }
    public CollectionStats Stats { get; set; }
    public IndexInfo Index { get; set; }
    public CleanInfo Clean { get; set; }
    public string CurrentSchemaFingerprint { get; set; }
}

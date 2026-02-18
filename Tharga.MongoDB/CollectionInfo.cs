using System;

namespace Tharga.MongoDB;

public record CollectionInfo : CollectionFingerprint
{
    public required string Server { get; init; }
    public required string DatabasePart { get; init; }
    public Source Source { get; set; }
    public required Registration Registration { get; init; }
    public required string[] Types { get; init; }
    public required Type CollectionType { get; init; }
    public int AccessCount { get; set; }
    public int CallCount { get; set; }
    public DocumentCount DocumentCount { get; set; }
    public long Size { get; set; }
    public IndexInfo Index { get; set; }
    public CleanInfo Clean { get; set; }
    public string CurrentSchemaFingerprint { get; set; }
}
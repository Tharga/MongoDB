using System;

namespace Tharga.MongoDB;

public record CollectionInfo : CollectionFingerprint
{
    public required string Server { get; init; }
    public string DatabasePart { get; init; }
    public Discovery Discovery { get; set; }
    public required Registration Registration { get; init; }
    public required string[] EntityTypes { get; init; }
    public required Type CollectionType { get; init; }

    /// <summary>
    /// Display name of <see cref="CollectionType"/> for cases where the <see cref="Type"/> isn't
    /// available — notably remote collections, whose type can't be serialized across the wire.
    /// Prefer <c>CollectionType?.Name ?? CollectionTypeName</c> when showing it.
    /// </summary>
    public string CollectionTypeName { get; init; }

    public CollectionStats Stats { get; set; }
    public IndexInfo Index { get; set; }
    public CleanInfo Clean { get; set; }
    public string CurrentSchemaFingerprint { get; set; }
}

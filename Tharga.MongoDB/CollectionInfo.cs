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

    /// <summary>
    /// Age of the data: when this snapshot was last reported by a remote agent or refreshed
    /// locally. Persisted in the <c>_monitor</c> cache so it survives restarts and lets the UI
    /// show how stale the information is. Null for entries that predate this tracking.
    /// </summary>
    public DateTime? ReportedAt { get; set; }
}

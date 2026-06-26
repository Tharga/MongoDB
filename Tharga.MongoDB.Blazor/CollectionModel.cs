using System;

namespace Tharga.MongoDB.Blazor;

public record CollectionModel : CollectionFingerprint
{
    public CollectionStats Stats { get; set; }
    public Discovery Discovery { get; set; }
    public required Registration Registration { get; set; }
    public required long Size { get; set; }
    public required IndexModel[] Indices { get; set; }
    public required bool? IndexEqualFields { get; set; }
    public CleanInfo Clean { get; set; }
    public int CallCount { get; set; }
    public string[] Sources { get; set; } = [];
    public bool IsLocal { get; init; }

    /// <summary>
    /// True while a background revalidation is in flight for this row.
    /// Per-cell components render a dimmed/italic style when set.
    /// </summary>
    public bool IsRevalidating { get; set; }

    /// <summary>
    /// Raised when a per-cell value on this model changed (e.g. CallCount, Stats).
    /// Per-cell components subscribe so they can re-render in isolation without
    /// triggering a grid-wide re-render. Not included in record equality (event
    /// backing field is private).
    /// </summary>
    public event Action Changed;

    public void NotifyChanged() => Changed?.Invoke();
}

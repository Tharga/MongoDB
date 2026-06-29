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

    /// <summary>
    /// String projection of <see cref="CollectionFingerprint.ConfigurationName"/> for the grid. Radzen builds
    /// filter/sort expressions against the bound property's type; binding the column to the <c>ConfigurationName</c>
    /// value object makes it create a string constant against that type ("Argument types do not match"), so the
    /// column binds here instead.
    /// </summary>
    public string ConfigurationNameText => ConfigurationName?.Value;

    /// <summary>The cluster host serving this collection's database. Used to identify the physical database across configurations.</summary>
    public string Server { get; init; }

    /// <summary>
    /// Every source (this server + agents) that has reported a collection in the same physical database
    /// (same <see cref="Server"/> + <see cref="CollectionFingerprint.DatabaseName"/>) — i.e. has a connection
    /// that could reach this collection, even via a different configuration name. Superset of <see cref="Sources"/>.
    /// </summary>
    public string[] DatabaseSources { get; set; } = [];

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

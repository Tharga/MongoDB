using MongoDB.Driver;

namespace Tharga.MongoDB.Lockable;

/// <summary>
/// Controls how a candidate document is chosen when acquiring a lock and several documents match.
/// </summary>
/// <remarks>
/// This affects <em>selection</em> only — which of the matching documents gets locked — not the lock itself.
/// Without it, an arbitrary matching document is locked, which makes ordered processing of a work queue impossible.
/// </remarks>
public record PickOptions<TEntity>
{
    /// <summary>
    /// Order applied to the matching documents; the first one in this order is locked.
    /// When null, an arbitrary matching document is locked.
    /// </summary>
    public SortDefinition<TEntity> Sort { get; init; }
}

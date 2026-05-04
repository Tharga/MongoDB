using System;
using System.Linq.Expressions;

namespace Tharga.MongoDB.Paging;

/// <summary>
/// Opaque cursor identifying a boundary document in a keyset-paginated query.
/// Carries the sort-field path, sort direction, the sort-field value at the boundary, and the
/// document <c>_id</c> (used as a tiebreaker on non-unique sort fields).
/// Round-trip via <see cref="ToString"/> + <see cref="Parse"/> for URL-safe storage.
/// </summary>
public readonly struct CursorToken : IEquatable<CursorToken>
{
    private readonly string _encoded;

    internal CursorToken(string encoded)
    {
        _encoded = encoded;
    }

    /// <summary>Sentinel for "no cursor" — used as the boundary value in <see cref="CursorPage{T}"/> when there are no items.</summary>
    public static CursorToken Empty => new(null);

    /// <summary>True when this token is the <see cref="Empty"/> sentinel.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(_encoded);

    /// <summary>URL-safe string form. Round-trips with <see cref="Parse"/>.</summary>
    public override string ToString() => _encoded ?? string.Empty;

    /// <summary>
    /// Parse a cursor from its <see cref="ToString"/> form. Throws <see cref="FormatException"/> for malformed input.
    /// </summary>
    public static CursorToken Parse(string s)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Construct a cursor that points at <paramref name="entity"/> under the given sort. Used by adapter code
    /// that falls back to skip-based queries (e.g. arbitrary page-number jumps) and then needs to resume keyset
    /// navigation from the boundary documents.
    /// </summary>
    public static CursorToken From<TEntity, TKey>(
        TEntity entity,
        Expression<Func<TEntity, object>> sortBy,
        bool ascending)
        where TEntity : EntityBase<TKey>
    {
        throw new NotImplementedException();
    }

    public bool Equals(CursorToken other) => string.Equals(_encoded, other._encoded, StringComparison.Ordinal);
    public override bool Equals(object obj) => obj is CursorToken other && Equals(other);
    public override int GetHashCode() => _encoded?.GetHashCode(StringComparison.Ordinal) ?? 0;
    public static bool operator ==(CursorToken left, CursorToken right) => left.Equals(right);
    public static bool operator !=(CursorToken left, CursorToken right) => !left.Equals(right);
}

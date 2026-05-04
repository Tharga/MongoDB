using System;
using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

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
    private readonly string _sortFieldPath;
    private readonly bool _ascending;
    private readonly BsonValue _sortValue;
    private readonly BsonValue _id;

    internal CursorToken(string sortFieldPath, bool ascending, BsonValue sortValue, BsonValue id)
    {
        _sortFieldPath = sortFieldPath;
        _ascending = ascending;
        _sortValue = sortValue ?? BsonNull.Value;
        _id = id ?? BsonNull.Value;

        var doc = new BsonDocument
        {
            ["f"] = sortFieldPath,
            ["d"] = ascending ? 1 : -1,
            ["v"] = _sortValue,
            ["i"] = _id,
        };
        _encoded = ToBase64Url(doc.ToBson());
    }

    /// <summary>Sentinel for "no cursor" — used as the boundary value in <see cref="CursorPage{T}"/> when there are no items.</summary>
    public static CursorToken Empty => default;

    /// <summary>True when this token is the <see cref="Empty"/> sentinel.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(_encoded);

    internal string SortFieldPath => _sortFieldPath;
    internal bool Ascending => _ascending;
    internal BsonValue SortValue => _sortValue;
    internal BsonValue Id => _id;

    /// <summary>URL-safe string form. Round-trips with <see cref="Parse"/>.</summary>
    public override string ToString() => _encoded ?? string.Empty;

    /// <summary>
    /// Parse a cursor from its <see cref="ToString"/> form. Throws <see cref="FormatException"/> for malformed input.
    /// An empty or null string returns <see cref="Empty"/>.
    /// </summary>
    public static CursorToken Parse(string s)
    {
        if (string.IsNullOrEmpty(s)) return Empty;

        byte[] bytes;
        try
        {
            bytes = FromBase64Url(s);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FormatException("Invalid cursor: encoding is not valid Base64URL.", ex);
        }

        BsonDocument doc;
        try
        {
            doc = BsonSerializer.Deserialize<BsonDocument>(bytes);
        }
        catch (Exception ex)
        {
            throw new FormatException("Invalid cursor: bytes are not a valid BSON document.", ex);
        }

        if (!doc.Contains("f") || !doc.Contains("d") || !doc.Contains("v") || !doc.Contains("i"))
            throw new FormatException("Invalid cursor: missing one or more required fields (f, d, v, i).");

        string path;
        int direction;
        try
        {
            path = doc["f"].AsString;
            direction = doc["d"].AsInt32;
        }
        catch (Exception ex)
        {
            throw new FormatException("Invalid cursor: field 'f' must be a string and 'd' must be an int32.", ex);
        }

        if (direction != 1 && direction != -1)
            throw new FormatException($"Invalid cursor: direction must be 1 or -1, got {direction}.");

        return new CursorToken(path, direction == 1, doc["v"], doc["i"]);
    }

    /// <summary>
    /// Construct a cursor that points at <paramref name="entity"/> under the given sort. Used by adapter code
    /// that falls back to skip-based queries (e.g. arbitrary page-number jumps) and then needs to resume keyset
    /// navigation from the boundary documents. <paramref name="sortBy"/> = <c>null</c> creates an _id-only cursor.
    /// </summary>
    public static CursorToken From<TEntity, TKey>(
        TEntity entity,
        Expression<Func<TEntity, object>> sortBy,
        bool ascending)
        where TEntity : EntityBase<TKey>
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));

        var serializer = BsonSerializer.SerializerRegistry.GetSerializer<TEntity>();
        var args = new RenderArgs<TEntity>(serializer, BsonSerializer.SerializerRegistry);

        string path;
        BsonValue sortValue;
        if (sortBy == null)
        {
            path = "_id";
            sortValue = ToBsonValue(entity.Id);
        }
        else
        {
            var fieldDef = new ExpressionFieldDefinition<TEntity, object>(sortBy);
            path = fieldDef.Render(args).FieldName;
            var compiled = sortBy.Compile();
            sortValue = ToBsonValue(compiled(entity));
        }

        return new CursorToken(path, ascending, sortValue, ToBsonValue(entity.Id));
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if this cursor was issued for a different sort
    /// (different field path or direction) than the call now wants to use it for.
    /// </summary>
    internal void ValidateForSort(string expectedPath, bool expectedAscending)
    {
        if (IsEmpty) return;
        if (!string.Equals(_sortFieldPath, expectedPath, StringComparison.Ordinal) || _ascending != expectedAscending)
        {
            throw new InvalidOperationException(
                $"This cursor was issued for sort '{_sortFieldPath}' direction {(_ascending ? "asc" : "desc")}; " +
                $"the current call uses sort '{expectedPath}' direction {(expectedAscending ? "asc" : "desc")}. " +
                "Cursors are not transferable across sorts.");
        }
    }

    public bool Equals(CursorToken other) => string.Equals(_encoded, other._encoded, StringComparison.Ordinal);
    public override bool Equals(object obj) => obj is CursorToken other && Equals(other);
    public override int GetHashCode() => _encoded?.GetHashCode(StringComparison.Ordinal) ?? 0;
    public static bool operator ==(CursorToken left, CursorToken right) => left.Equals(right);
    public static bool operator !=(CursorToken left, CursorToken right) => !left.Equals(right);

    private static BsonValue ToBsonValue(object value)
    {
        if (value == null) return BsonNull.Value;
        return BsonValue.Create(value);
    }

    private static string ToBase64Url(byte[] bytes)
    {
        // RFC 4648 §5: replace + with -, / with _, drop trailing =
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] FromBase64Url(string s)
    {
        var b64 = s.Replace('-', '+').Replace('_', '/');
        switch (b64.Length % 4)
        {
            case 2: b64 += "=="; break;
            case 3: b64 += "="; break;
            case 0: break;
            default: throw new FormatException("Invalid Base64URL string length.");
        }
        return Convert.FromBase64String(b64);
    }
}

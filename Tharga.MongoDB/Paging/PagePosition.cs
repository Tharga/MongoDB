using System;

namespace Tharga.MongoDB.Paging;

/// <summary>
/// Where in a keyset-paginated stream <see cref="IRepositoryCollection{TEntity, TKey}"/>'s
/// <c>GetPageAsync</c> should resume from. Construct via the static factories
/// (<see cref="First"/>, <see cref="Last"/>, <see cref="After"/>, <see cref="Before"/>).
/// </summary>
public readonly struct PagePosition
{
    internal enum PositionKind { First, Last, After, Before }

    internal PositionKind Kind { get; }
    internal CursorToken Cursor { get; }
    internal int PageStep { get; }

    private PagePosition(PositionKind kind, CursorToken cursor, int pageStep)
    {
        Kind = kind;
        Cursor = cursor;
        PageStep = pageStep;
    }

    /// <summary>The first page of the (filtered) collection in the requested sort order.</summary>
    public static PagePosition First => new(PositionKind.First, CursorToken.Empty, 0);

    /// <summary>The last page of the (filtered) collection in the requested sort order.</summary>
    public static PagePosition Last => new(PositionKind.Last, CursorToken.Empty, 0);

    /// <summary>
    /// The page strictly after <paramref name="cursor"/>. <paramref name="pageStep"/> &gt; 0 skips that many
    /// pages forward (bounded "±2 pages" UI buttons; not for arbitrary jumps).
    /// </summary>
    public static PagePosition After(CursorToken cursor, int pageStep = 0)
    {
        if (pageStep < 0) throw new ArgumentOutOfRangeException(nameof(pageStep), "pageStep must be >= 0.");
        return new PagePosition(PositionKind.After, cursor, pageStep);
    }

    /// <summary>
    /// The page strictly before <paramref name="cursor"/>. <paramref name="pageStep"/> &gt; 0 skips that many
    /// pages backward.
    /// </summary>
    public static PagePosition Before(CursorToken cursor, int pageStep = 0)
    {
        if (pageStep < 0) throw new ArgumentOutOfRangeException(nameof(pageStep), "pageStep must be >= 0.");
        return new PagePosition(PositionKind.Before, cursor, pageStep);
    }
}

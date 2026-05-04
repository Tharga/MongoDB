namespace Tharga.MongoDB.Paging;

/// <summary>
/// One page of items from a keyset-paginated query. <see cref="FirstCursor"/> /
/// <see cref="LastCursor"/> are the boundary cursors for navigating one page back / forward.
/// <see cref="HasNext"/> / <see cref="HasPrevious"/> let UIs disable navigation buttons.
/// Total count is intentionally not included — call <c>CountAsync(predicate)</c> separately and cache it.
/// </summary>
public sealed record CursorPage<T>(
    T[] Items,
    CursorToken FirstCursor,
    CursorToken LastCursor,
    bool HasNext,
    bool HasPrevious);

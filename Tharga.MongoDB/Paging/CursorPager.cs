using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;

namespace Tharga.MongoDB.Paging;

/// <summary>
/// Grid-agnostic state helper that wraps <c>GetPageAsync</c> for grid components driven by
/// <c>(skip, pageSize)</c>. Encapsulates: the per-filter total-count cache, the previous page's
/// first/last cursors, and the Skip→<see cref="PagePosition"/> decoder. Falls back to skip-based
/// <c>GetManyAsync</c> for arbitrary page-number jumps and re-issues cursors via
/// <see cref="CursorToken.From{TEntity, TKey}"/> so subsequent prev/next stays on the keyset path.
/// Built only on the public Layer 1 API — same access any consumer has, no privileged paths.
/// </summary>
public sealed class CursorPager<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    private const int MaxJumpPages = 5;

    private readonly IRepositoryCollection<TEntity, TKey> _repository;

    private string _queryCacheKey;
    private long _totalCount;
    private int _previousSkip = -1;
    private int _previousPageSize = -1;
    private CursorToken _firstCursor = CursorToken.Empty;
    private CursorToken _lastCursor = CursorToken.Empty;

    public CursorPager(IRepositoryCollection<TEntity, TKey> repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// Load the page that starts at <paramref name="skip"/> and contains up to <paramref name="pageSize"/>
    /// items. Returns the items and the cached total count for the current filter.
    /// </summary>
    public async Task<(TEntity[] Items, long TotalCount)> LoadAsync(
        int skip,
        int pageSize,
        Expression<Func<TEntity, bool>> predicate = null,
        Expression<Func<TEntity, object>> sortBy = null,
        bool ascending = true,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip), "skip must be non-negative.");
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize), "pageSize must be > 0.");

        var queryKey = BuildQueryKey(predicate, sortBy, ascending);
        if (!string.Equals(_queryCacheKey, queryKey, StringComparison.Ordinal))
        {
            _totalCount = await _repository.CountAsync(predicate, cancellationToken);
            _queryCacheKey = queryKey;
            _firstCursor = CursorToken.Empty;
            _lastCursor = CursorToken.Empty;
            _previousSkip = -1;
            _previousPageSize = -1;
        }

        var position = ResolvePosition(skip, pageSize);
        TEntity[] items;

        if (position.HasValue)
        {
            var page = await _repository.GetPageAsync(pageSize, position.Value, predicate, sortBy, ascending, cancellationToken);
            items = page.Items;
            if (items.Length > 0)
            {
                _firstCursor = page.FirstCursor;
                _lastCursor = page.LastCursor;
            }
        }
        else
        {
            items = await FallbackFetchAsync(skip, pageSize, predicate, sortBy, ascending, cancellationToken);
            if (items.Length > 0)
            {
                _firstCursor = CursorToken.From<TEntity, TKey>(items[0], sortBy, ascending);
                _lastCursor = CursorToken.From<TEntity, TKey>(items[items.Length - 1], sortBy, ascending);
            }
        }

        _previousSkip = skip;
        _previousPageSize = pageSize;
        return (items, _totalCount);
    }

    /// <summary>
    /// Forget cached cursors and total count. Call this when the filter is cleared or the underlying data
    /// is known to have changed underfoot.
    /// </summary>
    public void Reset()
    {
        _queryCacheKey = null;
        _totalCount = 0;
        _previousSkip = -1;
        _previousPageSize = -1;
        _firstCursor = CursorToken.Empty;
        _lastCursor = CursorToken.Empty;
    }

    private PagePosition? ResolvePosition(int skip, int pageSize)
    {
        if (skip == 0) return PagePosition.First;

        if (_totalCount > 0 && skip + pageSize >= _totalCount && pageSize == _previousPageSize)
        {
            // Only short-circuit to Last when the request lines up with the trailing-partial-page boundary.
            // Otherwise let it fall through to After/Before/fallback so the items are returned at the
            // requested skip rather than from the end.
            var lastPageSkip = (int)Math.Max(0, ((_totalCount - 1) / pageSize) * pageSize);
            if (skip == lastPageSkip) return PagePosition.Last;
        }

        if (_previousSkip < 0 || pageSize != _previousPageSize) return null;
        if (_firstCursor.IsEmpty || _lastCursor.IsEmpty) return null;

        var delta = skip - _previousSkip;
        if (delta == 0) return null;
        if (delta % pageSize != 0) return null;

        var stepMagnitude = Math.Abs(delta) / pageSize;
        if (stepMagnitude == 0 || stepMagnitude > MaxJumpPages) return null;

        if (delta > 0) return PagePosition.After(_lastCursor, stepMagnitude - 1);
        return PagePosition.Before(_firstCursor, stepMagnitude - 1);
    }

    private async Task<TEntity[]> FallbackFetchAsync(
        int skip,
        int pageSize,
        Expression<Func<TEntity, bool>> predicate,
        Expression<Func<TEntity, object>> sortBy,
        bool ascending,
        CancellationToken cancellationToken)
    {
        var sortDef = BuildSortDefinition(sortBy, ascending);
        var options = new Options<TEntity>
        {
            Skip = skip,
            Limit = pageSize,
            Sort = sortDef,
        };
        var result = await _repository.GetManyAsync(predicate, options, cancellationToken);
        return result.Items ?? Array.Empty<TEntity>();
    }

    private static SortDefinition<TEntity> BuildSortDefinition(Expression<Func<TEntity, object>> sortBy, bool ascending)
    {
        var b = Builders<TEntity>.Sort;
        if (sortBy == null)
        {
            return ascending ? b.Ascending("_id") : b.Descending("_id");
        }
        var sortField = ascending ? b.Ascending(sortBy) : b.Descending(sortBy);
        var idField = ascending ? b.Ascending("_id") : b.Descending("_id");
        return b.Combine(sortField, idField);
    }

    private static string BuildQueryKey(
        Expression<Func<TEntity, bool>> predicate,
        Expression<Func<TEntity, object>> sortBy,
        bool ascending)
    {
        var p = predicate?.ToString() ?? "<null>";
        var s = sortBy?.ToString() ?? "<null>";
        return $"{p}|{s}|{(ascending ? "asc" : "desc")}";
    }
}

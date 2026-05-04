using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

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
    private readonly IRepositoryCollection<TEntity, TKey> _repository;

    public CursorPager(IRepositoryCollection<TEntity, TKey> repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// Load the page that starts at <paramref name="skip"/> and contains up to <paramref name="pageSize"/>
    /// items. Returns the items and the cached total count for the current filter.
    /// </summary>
    public Task<(TEntity[] Items, long TotalCount)> LoadAsync(
        int skip,
        int pageSize,
        Expression<Func<TEntity, bool>> predicate = null,
        Expression<Func<TEntity, object>> sortBy = null,
        bool ascending = true,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Forget cached cursors and total count. Call this when the filter is cleared or the underlying data
    /// is known to have changed underfoot.
    /// </summary>
    public void Reset()
    {
        throw new NotImplementedException();
    }
}

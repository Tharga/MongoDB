using System.Collections.Generic;

namespace Tharga.MongoDB;

public record ResultPage<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    public IAsyncEnumerable<TEntity> Items { get; init; }
    public long TotalCount { get; init; }
    public int Page { get; init; }
    public int TotalPages { get; init; }
}
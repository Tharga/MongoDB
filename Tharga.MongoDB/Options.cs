using System;
using MongoDB.Driver;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

public record Options<TEntity>
{
    public ProjectionDefinition<TEntity> Projection { get; init; }
    public SortDefinition<TEntity> Sort { get; init; }
    public int? Limit { get; init; }
    public int? Skip { get; init; }

    public static implicit operator Options<TEntity>(FindOptions<TEntity> item)
    {
        return new Options<TEntity>
        {
            Limit = item.Limit,
            Sort = item.Sort,
            Skip = item.Skip,
        };
    }
}
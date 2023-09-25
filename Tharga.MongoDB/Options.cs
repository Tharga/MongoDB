using MongoDB.Driver;

namespace Tharga.MongoDB;

public record Options<TEntity>
{
    public ProjectionDefinition<TEntity> Projection { get; init; }
    public SortDefinition<TEntity> Sort { get; init; }
    public int? Limit { get; init; }
    public int? Skip { get; init; }
}
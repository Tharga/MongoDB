using MongoDB.Driver;

namespace Tharga.MongoDB.Disk;

public record Options<TEntity>
{
    public ProjectionDefinition<TEntity> Projection { get; init; }
    public SortDefinition<TEntity> Sort { get; init; }
    public int? Limit { get; init; }
}
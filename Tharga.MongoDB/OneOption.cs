using MongoDB.Driver;

namespace Tharga.MongoDB;

public record OneOption<TEntity>
{
    public SortDefinition<TEntity> Sort { get; init; }
    public EMode Mode { get; init; } = EMode.Single;

    public static OneOption<TEntity> First => new() { Mode = EMode.First };
    public static OneOption<TEntity> Single => new() { Mode = EMode.Single };
}
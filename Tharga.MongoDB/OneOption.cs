using MongoDB.Driver;

namespace Tharga.MongoDB;

public record OneOption<TEntity>
{
    public SortDefinition<TEntity> Sort { get; init; }
    public EMode Mode { get; init; } = EMode.FirstOrDefault;

    public static OneOption<TEntity> SingleOrDefault => new() { Mode = EMode.SingleOrDefault };
    public static OneOption<TEntity> Single => new() { Mode = EMode.Single };
    public static OneOption<TEntity> FirstOrDefault => new() { Mode = EMode.FirstOrDefault };
    public static OneOption<TEntity> First => new() { Mode = EMode.First };

    //TODO: Implicit convertion to findOption
}
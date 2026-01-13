namespace Tharga.MongoDB;

public record Result<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    public TEntity[] Items { get; init; }
    public int TotalCount { get; init; }
}

public record Result<TEntity>
{
    public TEntity[] Items { get; init; }
    public int TotalCount { get; init; }
}
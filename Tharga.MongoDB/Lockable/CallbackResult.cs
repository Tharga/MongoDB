namespace Tharga.MongoDB.Lockable;

public record CallbackResult<TEntity>
{
    public required bool Commit { get; init; }
    public required TEntity Before { get; init; }
    public required TEntity After { get; init; }
}
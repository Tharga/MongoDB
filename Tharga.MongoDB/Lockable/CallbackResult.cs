namespace Tharga.MongoDB.Lockable;

public record CallbackResult<TEntity>
{
    public required LockAction LockAction { get; init; }
    public required TEntity Before { get; init; }
    public required TEntity After { get; init; }
}
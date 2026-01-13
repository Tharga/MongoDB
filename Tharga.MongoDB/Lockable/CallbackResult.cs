using System;

namespace Tharga.MongoDB.Lockable;

public record CallbackResult<TEntity>
{
    [Obsolete($"Use {nameof(LockAction)} instead.")]
    public required bool Commit { get; init; }
    public required LockAction LockAction { get; init; }
    public required TEntity Before { get; init; }
    public required TEntity After { get; init; }
}
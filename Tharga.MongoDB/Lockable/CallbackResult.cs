namespace Tharga.MongoDB.Lockable;

public record CallbackResult<TEntity>
{
    //TODO: Replace commit with type (Abandon, Commit and Exception)
    //TODO: Also add action NoChange, Updated, Deleted.
    public required bool Commit { get; init; }
    //TODO: Add more details... public required ReleaseType ReleaseType { get; init; }
    public required TEntity Before { get; init; }
    public required TEntity After { get; init; }
}
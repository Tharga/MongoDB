namespace Tharga.MongoDB.Lockable;

public static class LockActionExtensions
{
    public static bool IsCommitted(this LockAction item)
    {
        return item == LockAction.CommitDeleted || item == LockAction.CommitUpdated;
    }
}
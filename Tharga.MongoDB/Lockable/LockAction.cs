namespace Tharga.MongoDB.Lockable;

public enum LockAction
{
    Locked,
    Abandoned,
    CommitUpdated,
    CommitDeleted,
    Exception
}
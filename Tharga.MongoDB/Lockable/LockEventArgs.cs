using System;

namespace Tharga.MongoDB.Lockable;

public class LockEventArgs<TEntity> : EventArgs
{
    public LockEventArgs(TEntity entity, LockAction lockAction)
    {
        Entity = entity;
        LockAction = lockAction;
    }

    public TEntity Entity { get; }
    public LockAction LockAction { get; }
}
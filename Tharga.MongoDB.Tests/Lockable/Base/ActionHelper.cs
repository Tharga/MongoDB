using System;
using System.Threading.Tasks;
using MongoDB.Bson;
using Tharga.MongoDB.Lockable;
using Tharga.MongoDB.Tests.Support;

namespace Tharga.MongoDB.Tests.Lockable.Base;

public static class ActionHelper
{
    public static Func<Task> Action(EndAction first, EntityScope<LockableTestEntity, ObjectId> scope)
    {
        Func<Task> firstAct;
        switch (first)
        {
            case EndAction.Abandon:
                firstAct = scope.AbandonAsync;
                break;
            case EndAction.Commit:
                firstAct = () => scope.CommitAsync();
                break;
            case EndAction.Exception:
                firstAct = () => scope.SetErrorStateAsync(new Exception("Some issue."));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(first), first, null);
        }

        return firstAct;
    }

    public enum EndAction
    {
        Abandon,
        Commit,
        Exception
    }
}
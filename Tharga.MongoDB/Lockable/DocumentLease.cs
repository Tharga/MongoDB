using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;

namespace Tharga.MongoDB.Lockable;

/// <summary>
/// Lease over multiple locked documents. Per-document commit decisions are staged via
/// <c>MarkForUpdate</c>, <see cref="MarkForDelete"/>, or <see cref="MarkRelease"/>,
/// and applied sequentially on <see cref="CommitAsync"/>. Disposal without commit releases all
/// not-yet-marked locks.
/// </summary>
public sealed class DocumentLease<T> : DocumentLease<T, ObjectId>
    where T : LockableEntityBase<ObjectId>
{
    internal DocumentLease() { }
}

/// <summary>
/// Lease over multiple locked documents. Per-document commit decisions are staged via
/// <c>MarkForUpdate</c>, <see cref="MarkForDelete"/>, or <see cref="MarkRelease"/>,
/// and applied sequentially on <see cref="CommitAsync"/>. Disposal without commit releases all
/// not-yet-marked locks.
/// </summary>
public class DocumentLease<T, TKey> : IAsyncDisposable, IDisposable
    where T : LockableEntityBase<TKey>
{
    internal DocumentLease() { }

    /// <summary>The locked documents at the time of acquisition.</summary>
    public virtual IReadOnlyList<T> Documents => throw new NotImplementedException();

    /// <summary>Stage an update for the document whose <c>Id</c> matches <paramref name="updated"/>.<c>Id</c>.</summary>
    public virtual void MarkForUpdate(T updated) => throw new NotImplementedException();

    /// <summary>Stage an update for the document with the given <paramref name="id"/>, replacing it with <paramref name="updated"/>.</summary>
    public virtual void MarkForUpdate(TKey id, T updated) => throw new NotImplementedException();

    /// <summary>Stage a delete for the document with the given <paramref name="id"/>.</summary>
    public virtual void MarkForDelete(TKey id) => throw new NotImplementedException();

    /// <summary>Stage an explicit release-unchanged for the document with the given <paramref name="id"/> (the default for unmarked docs).</summary>
    public virtual void MarkRelease(TKey id) => throw new NotImplementedException();

    /// <summary>
    /// Apply all staged decisions sequentially. Failures are collected into the returned summary's
    /// <see cref="DocumentLeaseCommitSummary{TKey}.Failures"/> list rather than thrown — remaining decisions still attempt to apply.
    /// </summary>
    public virtual Task<DocumentLeaseCommitSummary<TKey>> CommitAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    /// <summary>Release all locks that have not been released by a prior <see cref="CommitAsync"/>.</summary>
    public virtual ValueTask DisposeAsync() => throw new NotImplementedException();

    public virtual void Dispose() => throw new NotImplementedException();
}

/// <summary>Summary returned from <see cref="DocumentLease{T, TKey}.CommitAsync"/>.</summary>
public record DocumentLeaseCommitSummary<TKey>
{
    /// <summary>Number of documents committed via <see cref="CommitMode.Update"/>.</summary>
    public required int Updated { get; init; }

    /// <summary>Number of documents committed via <see cref="CommitMode.Delete"/>.</summary>
    public required int Deleted { get; init; }

    /// <summary>Number of documents released without changes (explicitly marked or unmarked).</summary>
    public required int ReleasedUnchanged { get; init; }

    /// <summary>Per-document commit failures collected during the sequential commit pass.</summary>
    public required IReadOnlyList<DocumentLeaseFailure<TKey>> Failures { get; init; }
}

/// <summary>One commit failure inside a <see cref="DocumentLease{T, TKey}"/>.</summary>
public record DocumentLeaseFailure<TKey>
{
    /// <summary>The id of the document whose decision failed to apply.</summary>
    public required TKey Id { get; init; }

    /// <summary>The decision that was attempted. <c>null</c> if the failure occurred during the release-unchanged path.</summary>
    public required CommitMode? IntendedDecision { get; init; }

    /// <summary>Human-readable failure message.</summary>
    public required string Error { get; init; }
}

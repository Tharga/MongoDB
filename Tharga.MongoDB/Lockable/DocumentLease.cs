using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Tharga.MongoDB.Lockable;

/// <summary>
/// Hook the lease uses to wrap its commit pass in a transaction when
/// <see cref="DocumentLease{T, TKey}.CommitAsync"/> is called with <c>transactional: true</c>.
/// </summary>
internal interface IDocumentLeaseTransactionRunner
{
    Task RunInTransactionAsync(Func<IClientSessionHandle, CancellationToken, Task> body, CancellationToken cancellationToken);
}

/// <summary>
/// Internal per-document handle inside a <see cref="DocumentLease{T, TKey}"/>: the locked entity and a
/// release-action delegate that dispatches on the <see cref="CommitMode"/> chosen at commit time
/// (or <c>null</c> for the abandon / error-state path). The fourth parameter carries the session to use for
/// the underlying writes — <c>null</c> means non-transactional.
/// </summary>
internal sealed record DocumentLeaseEntry<T, TKey>(T Entity, Func<T, CommitMode?, Exception, IClientSessionHandle, Task> ReleaseAction)
    where T : LockableEntityBase<TKey>;

/// <summary>
/// Lease over multiple locked documents. Per-document commit decisions are staged via
/// <c>MarkForUpdate</c>, <c>MarkForDelete</c>, or <c>MarkRelease</c>,
/// and applied sequentially on <c>CommitAsync</c>. Disposal without commit releases all
/// not-yet-marked locks.
/// </summary>
public sealed class DocumentLease<T> : DocumentLease<T, ObjectId>
    where T : LockableEntityBase<ObjectId>
{
    internal DocumentLease(IReadOnlyList<DocumentLeaseEntry<T, ObjectId>> entries, IClientSessionHandle session, IDocumentLeaseTransactionRunner transactionRunner)
        : base(entries, session, transactionRunner) { }
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
    private readonly IReadOnlyList<DocumentLeaseEntry<T, TKey>> _entries;
    private readonly Dictionary<TKey, DocumentLeaseEntry<T, TKey>> _byId;
    private readonly List<TKey> _markOrder = new();
    private readonly Dictionary<TKey, (CommitMode? Mode, T Updated)> _marks = new();
    private readonly HashSet<TKey> _applied = new();
    private readonly IClientSessionHandle _boundSession;
    private readonly IDocumentLeaseTransactionRunner _transactionRunner;
    private bool _committed;
    private bool _commitFailed;
    private bool _errorStateSet;
    private bool _disposed;

    internal DocumentLease(IReadOnlyList<DocumentLeaseEntry<T, TKey>> entries, IClientSessionHandle session = null, IDocumentLeaseTransactionRunner transactionRunner = null)
    {
        _entries = entries;
        _byId = entries.ToDictionary(e => e.Entity.Id);
        _boundSession = session;
        _transactionRunner = transactionRunner;
    }

    /// <summary>The locked documents at the time of acquisition, in lock-acquisition order.</summary>
    public IReadOnlyList<T> Documents
    {
        get
        {
            EnsureNotDisposed();
            return _entries.Select(e => e.Entity).ToList();
        }
    }

    /// <summary>Stage an update for the document whose <c>Id</c> matches <paramref name="updated"/>.<c>Id</c>.</summary>
    public void MarkForUpdate(T updated)
    {
        if (updated == null) throw new ArgumentNullException(nameof(updated));
        StageMark(updated.Id, CommitMode.Update, updated);
    }

    /// <summary>Stage an update for the document with the given <paramref name="id"/>, replacing it with <paramref name="updated"/>.</summary>
    public void MarkForUpdate(TKey id, T updated)
    {
        if (updated == null) throw new ArgumentNullException(nameof(updated));
        StageMark(id, CommitMode.Update, updated);
    }

    /// <summary>Stage a delete for the document with the given <paramref name="id"/>.</summary>
    public void MarkForDelete(TKey id)
    {
        StageMark(id, CommitMode.Delete, default);
    }

    /// <summary>
    /// Stage an explicit release-unchanged for the document with the given <paramref name="id"/>.
    /// Equivalent to leaving the document unmarked at commit time.
    /// </summary>
    public void MarkRelease(TKey id)
    {
        StageMark(id, mode: null, default);
    }

    private void StageMark(TKey id, CommitMode? mode, T updated)
    {
        EnsureNotCommitted();
        EnsureNotDisposed();
        if (!_byId.ContainsKey(id))
            throw new ArgumentException($"No document with id '{id}' is locked in this lease.", nameof(id));

        if (!_marks.ContainsKey(id))
            _markOrder.Add(id);
        _marks[id] = (mode, updated);
    }

    /// <summary>
    /// Apply all staged decisions in the order they were marked. Any not-yet-marked locks are released unchanged.
    /// </summary>
    /// <param name="transactional">
    /// When <c>false</c> (default), each decision is applied sequentially and per-decision failures are collected
    /// into <see cref="DocumentLeaseCommitSummary{TKey}.Failures"/> rather than thrown — remaining decisions still
    /// attempt to apply.
    /// When <c>true</c>, the entire commit pass runs inside a transaction so all decisions land atomically (or
    /// none do). If the lease was created with a bound session (caller is already inside an outer transaction)
    /// the bound session is reused. Any decision-time failure aborts the transaction and rethrows; on abort no
    /// decisions land and the locks remain held on the documents.
    /// </param>
    /// <param name="cancellationToken">Cancels between operations. Honored before each decision is applied.</param>
    public async Task<DocumentLeaseCommitSummary<TKey>> CommitAsync(bool transactional = false, CancellationToken cancellationToken = default)
    {
        EnsureNotCommitted();
        EnsureNotDisposed();
        _committed = true;

        try
        {
            if (transactional)
            {
                // Reuse the bound session if present; otherwise open a new transaction.
                if (_boundSession != null)
                {
                    return await ApplyDecisionsAsync(_boundSession, allOrNothing: true, cancellationToken);
                }

                if (_transactionRunner == null)
                    throw new InvalidOperationException("DocumentLease cannot start a transaction — no transaction runner was provided. This typically means the lease was constructed outside of LockableRepositoryCollectionBase.");

                DocumentLeaseCommitSummary<TKey> result = null;
                await _transactionRunner.RunInTransactionAsync(
                    async (session, ct) =>
                    {
                        result = await ApplyDecisionsAsync(session, allOrNothing: true, ct);
                    },
                    cancellationToken);
                return result;
            }

            return await ApplyDecisionsAsync(_boundSession, allOrNothing: false, cancellationToken);
        }
        catch
        {
            // Nothing landed and the locks are still held. Remember that, so the caller can still reach
            // SetErrorStateAsync, and so disposal releases the locks instead of leaving them to expire.
            _commitFailed = true;
            throw;
        }
    }

    private async Task<DocumentLeaseCommitSummary<TKey>> ApplyDecisionsAsync(IClientSessionHandle session, bool allOrNothing, CancellationToken cancellationToken)
    {
        var failures = new List<DocumentLeaseFailure<TKey>>();
        int updated = 0, deleted = 0, released = 0;
        var processed = new HashSet<TKey>();

        foreach (var id in _markOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (mode, markedUpdate) = _marks[id];
            var entry = _byId[id];
            try
            {
                var entityToCommit = markedUpdate ?? entry.Entity;
                await entry.ReleaseAction.Invoke(entityToCommit, mode, null, session);
                // A non-transactional decision is on disk the moment it succeeds, and the lock with it.
                // A transactional one is rolled back if a later decision fails, so the lock is still ours.
                if (!allOrNothing) _applied.Add(id);
                switch (mode)
                {
                    case CommitMode.Update: updated++; break;
                    case CommitMode.Delete: deleted++; break;
                    case null: released++; break;
                }
            }
            catch (Exception ex)
            {
                if (allOrNothing) throw;
                failures.Add(new DocumentLeaseFailure<TKey>
                {
                    Id = id,
                    IntendedDecision = mode,
                    Error = ex.Message,
                });
            }
            processed.Add(id);
        }

        // Release any remaining unmarked entries
        foreach (var entry in _entries)
        {
            if (processed.Contains(entry.Entity.Id)) continue;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await entry.ReleaseAction.Invoke(entry.Entity, null, null, session);
                if (!allOrNothing) _applied.Add(entry.Entity.Id);
                released++;
            }
            catch (Exception ex)
            {
                if (allOrNothing) throw;
                failures.Add(new DocumentLeaseFailure<TKey>
                {
                    Id = entry.Entity.Id,
                    IntendedDecision = null,
                    Error = ex.Message,
                });
            }
        }

        return new DocumentLeaseCommitSummary<TKey>
        {
            Updated = updated,
            Deleted = deleted,
            ReleasedUnchanged = released,
            Failures = failures,
        };
    }

    /// <summary>
    /// Records <paramref name="exception"/> on every document in the lease, putting them into error state — the
    /// same state <see cref="EntityScope{T, TKey}.SetErrorStateAsync"/> produces for a single document.
    /// Call it instead of <see cref="CommitAsync"/>, or after a transactional <see cref="CommitAsync"/> has
    /// failed, while the locks are still held. A document whose lock is no longer ours is never overwritten;
    /// it is reported in <see cref="DocumentLeaseErrorStateSummary{TKey}.Failures"/> instead.
    /// Every document is attempted: <paramref name="cancellationToken"/> is honored before the first write and
    /// not between them, so the lease never ends up half-marked.
    /// When the lease was created with a bound session the writes join that transaction, which means they are
    /// rolled back with it — a caller that wants the error state to survive must commit its own transaction,
    /// or create the lease without a session.
    /// </summary>
    public async Task<DocumentLeaseErrorStateSummary<TKey>> SetErrorStateAsync(Exception exception, CancellationToken cancellationToken = default)
    {
        if (exception == null) throw new ArgumentNullException(nameof(exception));
        EnsureNotDisposed();
        if (_errorStateSet) throw new InvalidOperationException("Error state has already been set on this lease.");
        if (_committed && !_commitFailed) throw new InvalidOperationException("Lease has already been committed.");
        cancellationToken.ThrowIfCancellationRequested();

        _committed = true;
        _errorStateSet = true;

        var failures = new List<DocumentLeaseFailure<TKey>>();
        var errorState = 0;

        foreach (var entry in _entries)
        {
            if (_applied.Contains(entry.Entity.Id)) continue;
            try
            {
                await entry.ReleaseAction.Invoke(entry.Entity, null, exception, _boundSession);
                errorState++;
            }
            catch (Exception ex)
            {
                failures.Add(new DocumentLeaseFailure<TKey>
                {
                    Id = entry.Entity.Id,
                    IntendedDecision = null,
                    Error = ex.Message,
                });
            }
        }

        return new DocumentLeaseErrorStateSummary<TKey>
        {
            ErrorState = errorState,
            Failures = failures,
        };
    }

    /// <summary>
    /// Releases any locks that have not been released by a prior <see cref="CommitAsync"/>, including the locks
    /// still held after a <see cref="CommitAsync"/> that failed. With a bound session those releases join the
    /// caller's transaction and are rolled back with it, leaving the locks in place until they expire.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_errorStateSet) return;
        if (_committed && !_commitFailed) return;

        foreach (var entry in _entries)
        {
            // Skip the ones a non-transactional commit already applied: their lock is gone, and releasing by id
            // again would clear whatever lock another owner has taken on the document since.
            if (_applied.Contains(entry.Entity.Id)) continue;

            try
            {
                await entry.ReleaseAction.Invoke(entry.Entity, null, null, _boundSession);
            }
            catch
            {
                // Best-effort release on dispose; matches EntityScope dispose semantics.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed || _errorStateSet) return;
        if (_committed && !_commitFailed) return;
        Task.Run(() => DisposeAsync().AsTask());
    }

    private void EnsureNotCommitted()
    {
        if (_committed) throw new InvalidOperationException("Lease has already been committed.");
    }

    private void EnsureNotDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DocumentLease<T, TKey>));
    }
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

/// <summary>Summary returned from <see cref="DocumentLease{T, TKey}.SetErrorStateAsync"/>.</summary>
public record DocumentLeaseErrorStateSummary<TKey>
{
    /// <summary>Number of documents that were put into error state.</summary>
    public required int ErrorState { get; init; }

    /// <summary>
    /// Documents that could not be put into error state, typically because the lock is no longer ours.
    /// </summary>
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

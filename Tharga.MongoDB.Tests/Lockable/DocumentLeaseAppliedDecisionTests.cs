using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Lockable;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable;

/// <summary>
/// A decision that a non-transactional commit has applied is on disk, and its lock with it. If the commit then
/// throws, disposal must leave that document alone — releasing it again would write over whatever lock another
/// owner has taken since. Driven through the lease's own seam, so the timing is exact and no mongod is needed.
/// </summary>
public class DocumentLeaseAppliedDecisionTests
{
    private record LeaseSeamEntity : LockableEntityBase
    {
        public string Data { get; init; }
    }

    private sealed record ReleaseCall(ObjectId Id, CommitMode? Mode, Exception Exception);

    private static (DocumentLease<LeaseSeamEntity, ObjectId> Lease, List<ReleaseCall> Calls, LeaseSeamEntity First, LeaseSeamEntity Second) BuildLease(Action onFirstRelease = null)
    {
        var first = new LeaseSeamEntity { Id = ObjectId.GenerateNewId(), Data = "first" };
        var second = new LeaseSeamEntity { Id = ObjectId.GenerateNewId(), Data = "second" };
        var calls = new List<ReleaseCall>();

        DocumentLeaseEntry<LeaseSeamEntity, ObjectId> BuildEntry(LeaseSeamEntity entity, bool isFirst)
        {
            Func<LeaseSeamEntity, CommitMode?, Exception, IClientSessionHandle, Task> releaseAction = (e, mode, exception, _) =>
            {
                calls.Add(new ReleaseCall(e.Id, mode, exception));
                if (isFirst) onFirstRelease?.Invoke();
                return Task.CompletedTask;
            };

            return new DocumentLeaseEntry<LeaseSeamEntity, ObjectId>(entity, releaseAction);
        }

        var lease = new DocumentLease<LeaseSeamEntity, ObjectId>([BuildEntry(first, true), BuildEntry(second, false)]);
        return (lease, calls, first, second);
    }

    [Fact]
    public async Task CommitCancelledAfterTheFirstDecision_DisposeReleasesOnlyTheUnappliedDocument()
    {
        using var cancelAfterFirst = new CancellationTokenSource();
        var (lease, calls, first, second) = BuildLease(onFirstRelease: () => cancelAfterFirst.Cancel());

        lease.MarkForUpdate(first with { Data = "first-updated" });
        lease.MarkForUpdate(second with { Data = "second-updated" });

        Func<Task> commit = async () => await lease.CommitAsync(cancellationToken: cancelAfterFirst.Token);
        await commit.Should().ThrowAsync<OperationCanceledException>();

        await lease.DisposeAsync();

        calls.Should().HaveCount(2);
        calls[0].Should().BeEquivalentTo(new ReleaseCall(first.Id, CommitMode.Update, null));
        // Only the document the commit never reached is released; the applied one is not touched again.
        calls[1].Should().BeEquivalentTo(new ReleaseCall(second.Id, null, null));
    }

    [Fact]
    public async Task SetErrorState_AfterACommitThatAppliedOneDecision_MarksOnlyTheUnappliedDocument()
    {
        using var cancelAfterFirst = new CancellationTokenSource();
        var (lease, calls, first, second) = BuildLease(onFirstRelease: () => cancelAfterFirst.Cancel());

        lease.MarkForUpdate(first with { Data = "first-updated" });
        lease.MarkForUpdate(second with { Data = "second-updated" });

        Func<Task> commit = async () => await lease.CommitAsync(cancellationToken: cancelAfterFirst.Token);
        await commit.Should().ThrowAsync<OperationCanceledException>();

        var exception = new InvalidOperationException("Merge failed");
        var summary = await lease.SetErrorStateAsync(exception);

        summary.Marked.Should().Be(1);
        summary.Failures.Should().BeEmpty();
        calls.Should().HaveCount(2);
        calls[1].Should().BeEquivalentTo(new ReleaseCall(second.Id, null, exception));
    }

    [Fact]
    public async Task SetErrorState_WithACancelledToken_MarksNothing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var (lease, calls, _, _) = BuildLease();

        Func<Task> act = async () => await lease.SetErrorStateAsync(new InvalidOperationException("Merge failed"), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().BeEmpty();

        // The lease is untouched, so the locks are still released on disposal.
        await lease.DisposeAsync();
        calls.Should().HaveCount(2);
        calls.Should().OnlyContain(x => x.Mode == null && x.Exception == null);
    }

    [Fact]
    public async Task SetErrorState_MarksEveryDocumentEvenWhenOneFails()
    {
        var first = new LeaseSeamEntity { Id = ObjectId.GenerateNewId(), Data = "first" };
        var second = new LeaseSeamEntity { Id = ObjectId.GenerateNewId(), Data = "second" };
        var marked = new List<ObjectId>();

        DocumentLeaseEntry<LeaseSeamEntity, ObjectId> BuildEntry(LeaseSeamEntity entity, bool fails)
        {
            Func<LeaseSeamEntity, CommitMode?, Exception, IClientSessionHandle, Task> releaseAction = (e, _, _, _) =>
            {
                if (fails) throw new UnlockDifferentEntityException("Lock key missmatch.");
                marked.Add(e.Id);
                return Task.CompletedTask;
            };

            return new DocumentLeaseEntry<LeaseSeamEntity, ObjectId>(entity, releaseAction);
        }

        var lease = new DocumentLease<LeaseSeamEntity, ObjectId>([BuildEntry(first, true), BuildEntry(second, false)]);

        var summary = await lease.SetErrorStateAsync(new InvalidOperationException("Merge failed"));

        summary.Marked.Should().Be(1);
        summary.Failures.Should().ContainSingle(x => x.Id == first.Id);
        marked.Should().ContainSingle(x => x == second.Id);
    }
}

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Lockable;
using Tharga.MongoDB.Tests.Lockable.Base;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable;

[Collection("Sequential")]
public class SetErrorStateLeaseTests : LockableTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task SetErrorState_PutsEveryDocumentInErrorState()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var a = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "a" };
        var b = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "b" };
        await sut.AddAsync(a);
        await sut.AddAsync(b);

        await using var lease = await sut.LockManyAsync([a.Id, b.Id]);

        var summary = await lease.SetErrorStateAsync(new InvalidOperationException("Oups"));

        summary.ErrorState.Should().Be(2);
        summary.Failures.Should().BeEmpty();

        foreach (var id in new[] { a.Id, b.Id })
        {
            var after = await sut.GetOneAsync(id);
            after.Lock.Should().NotBeNull();
            after.Lock.ExceptionInfo.Message.Should().Be("Oups");
        }
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task SetErrorState_LeavesTheDocumentsUnchanged()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var a = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "a" };
        await sut.AddAsync(a);

        await using (var lease = await sut.LockManyAsync([a.Id]))
        {
            lease.MarkForUpdate(lease.Documents.Single() with { Data = "never-committed" });
            await lease.SetErrorStateAsync(new InvalidOperationException("Oups"));
        }

        (await sut.GetOneAsync(a.Id)).Data.Should().Be("a");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task SetErrorState_DoesNotOverwriteALockHeldBySomeoneElse()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var mine = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "mine" };
        var stolen = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "stolen" };
        await sut.AddAsync(mine);
        await sut.AddAsync(stolen);

        await using var lease = await sut.LockManyAsync([mine.Id, stolen.Id]);

        // Someone force-releases one of our locks and takes it for themselves.
        await sut.ReleaseOneAsync(stolen.Id, ReleaseMode.Any);
        await using var otherOwner = await sut.LockAsync(stolen.Id, actor: "OtherActor");

        var summary = await lease.SetErrorStateAsync(new InvalidOperationException("Oups"));

        summary.ErrorState.Should().Be(1);
        summary.Failures.Should().ContainSingle(x => x.Id == stolen.Id);

        (await sut.GetOneAsync(mine.Id)).Lock.ExceptionInfo.Message.Should().Be("Oups");

        var otherLock = (await sut.GetOneAsync(stolen.Id)).Lock;
        otherLock.Should().NotBeNull();
        otherLock.Actor.Should().Be("OtherActor");
        otherLock.ExceptionInfo.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    [Trait("Category", "RequiresReplicaSet")]
    public async Task SetErrorState_AfterFailedTransactionalCommit_MarksTheStillHeldDocuments()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var a = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "a" };
        var b = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "b" };
        await sut.AddAsync(a);
        await sut.AddAsync(b);

        await using var lease = await sut.LockManyAsync([a.Id, b.Id]);
        lease.MarkForUpdate(lease.Documents.Single(x => x.Id == a.Id) with { Data = "a-updated" });
        lease.MarkForDelete(b.Id);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Func<Task> commit = async () => await lease.CommitAsync(transactional: true, cancelled.Token);
        await commit.Should().ThrowAsync<OperationCanceledException>();

        var summary = await lease.SetErrorStateAsync(new InvalidOperationException("Merge failed"));

        summary.ErrorState.Should().Be(2);
        summary.Failures.Should().BeEmpty();

        var postA = await sut.GetOneAsync(a.Id);
        postA.Data.Should().Be("a");
        postA.Lock.ExceptionInfo.Message.Should().Be("Merge failed");

        var postB = await sut.GetOneAsync(b.Id);
        postB.Should().NotBeNull();
        postB.Lock.ExceptionInfo.Message.Should().Be("Merge failed");
    }

    [Fact]
    [Trait("Category", "Database")]
    [Trait("Category", "RequiresReplicaSet")]
    public async Task TransactionalCommitFailingOnTheSecondDecision_RollsBackTheFirstAndKeepsTheLock()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var first = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "first" };
        var second = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "second" };
        await sut.AddAsync(first);
        await sut.AddAsync(second);

        await using var lease = await sut.LockManyAsync([first.Id, second.Id]);
        lease.MarkForUpdate(lease.Documents.Single(x => x.Id == first.Id) with { Data = "first-updated" });
        lease.MarkForUpdate(lease.Documents.Single(x => x.Id == second.Id) with { Data = "second-updated" });

        // Someone takes the second document from us, so committing it fails after the first one has been written.
        await sut.ReleaseOneAsync(second.Id, ReleaseMode.Any);
        await using var otherOwner = await sut.LockAsync(second.Id, actor: "OtherActor");

        Func<Task> commit = async () => await lease.CommitAsync(transactional: true);
        await commit.Should().ThrowAsync<Exception>();

        // The first update was rolled back with the transaction, and its lock is still ours to mark.
        var postFirst = await sut.GetOneAsync(first.Id);
        postFirst.Data.Should().Be("first");
        postFirst.Lock.Should().NotBeNull();

        var summary = await lease.SetErrorStateAsync(new InvalidOperationException("Merge failed"));

        summary.ErrorState.Should().Be(1);
        summary.Failures.Should().ContainSingle(x => x.Id == second.Id);
        (await sut.GetOneAsync(first.Id)).Lock.ExceptionInfo.Message.Should().Be("Merge failed");

        var otherLock = (await sut.GetOneAsync(second.Id)).Lock;
        otherLock.Actor.Should().Be("OtherActor");
        otherLock.ExceptionInfo.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    [Trait("Category", "RequiresReplicaSet")]
    public async Task FailedTransactionalCommit_DisposeReleasesTheLocks()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var a = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "a" };
        await sut.AddAsync(a);

        await using (var lease = await sut.LockManyAsync([a.Id]))
        {
            lease.MarkForUpdate(lease.Documents.Single() with { Data = "a-updated" });

            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();
            Func<Task> commit = async () => await lease.CommitAsync(transactional: true, cancelled.Token);
            await commit.Should().ThrowAsync<OperationCanceledException>();
        }

        var post = await sut.GetOneAsync(a.Id);
        post.Data.Should().Be("a");
        post.Lock.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task SetErrorState_AfterSuccessfulCommit_Throws()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var a = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "a" };
        await sut.AddAsync(a);

        await using var lease = await sut.LockManyAsync([a.Id]);
        await lease.CommitAsync();

        Func<Task> act = async () => await lease.SetErrorStateAsync(new InvalidOperationException("Oups"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task SetErrorState_CalledTwice_Throws()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var a = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "a" };
        await sut.AddAsync(a);

        await using var lease = await sut.LockManyAsync([a.Id]);
        await lease.SetErrorStateAsync(new InvalidOperationException("Oups"));

        Func<Task> act = async () => await lease.SetErrorStateAsync(new InvalidOperationException("Oups again"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}

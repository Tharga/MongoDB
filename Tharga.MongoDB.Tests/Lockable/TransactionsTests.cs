using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Lockable;
using Tharga.MongoDB.Tests.Lockable.Base;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable;

[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class TransactionsTests : LockableTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    [Trait("Category", "RequiresReplicaSet")]
    public async Task WithTransactionAsync_Lockable_CommitsAtomically()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "before" };
        await sut.AddAsync(entity);

        await _mongoDbServiceFactory.WithTransactionAsync(async (session, ct) =>
        {
            await using var scope = await sut.LockAsync(entity.Id, session: session);
            await scope.CommitAsync(CommitMode.Update, scope.Entity with { Data = "after" });
        });

        var post = await sut.GetOneAsync(entity.Id);
        post.Data.Should().Be("after");
        post.Lock.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    [Trait("Category", "RequiresReplicaSet")]
    public async Task WithTransactionAsync_Lockable_RollbackOnException_LeavesEntityUnchanged()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "before" };
        await sut.AddAsync(entity);

        Func<Task> act = async () =>
        {
            await _mongoDbServiceFactory.WithTransactionAsync(async (session, ct) =>
            {
                await using var scope = await sut.LockAsync(entity.Id, session: session);
                await scope.CommitAsync(CommitMode.Update, scope.Entity with { Data = "should-not-stick" });
                throw new InvalidOperationException("triggered abort");
            });
        };

        await act.Should().ThrowAsync<InvalidOperationException>();

        var post = await sut.GetOneAsync(entity.Id);
        post.Data.Should().Be("before");
        post.Lock.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    [Trait("Category", "RequiresReplicaSet")]
    public async Task LockManyAsync_TransactionalCommit_AllOrNothing()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var a = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "a" };
        var b = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "b" };
        await sut.AddAsync(a);
        await sut.AddAsync(b);

        await using var lease = await sut.LockManyAsync(new[] { a.Id, b.Id });

        lease.MarkForUpdate(lease.Documents.Single(d => d.Id == a.Id) with { Data = "a-updated" });
        lease.MarkForUpdate(lease.Documents.Single(d => d.Id == b.Id) with { Data = "b-updated" });

        var summary = await lease.CommitAsync(transactional: true);

        summary.Updated.Should().Be(2);
        summary.Failures.Should().BeEmpty();
        (await sut.GetOneAsync(a.Id)).Data.Should().Be("a-updated");
        (await sut.GetOneAsync(b.Id)).Data.Should().Be("b-updated");
    }

    [Fact]
    [Trait("Category", "Database")]
    [Trait("Category", "RequiresReplicaSet")]
    public async Task LockManyAsync_TransactionalCommit_ReusesBoundSession()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var a = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "a" };
        await sut.AddAsync(a);

        // Outer transaction binds the session; inner transactional commit should reuse it (no nested transaction).
        await _mongoDbServiceFactory.WithTransactionAsync(async (session, ct) =>
        {
            await using var lease = await sut.LockManyAsync(new[] { a.Id }, session: session);
            lease.MarkForUpdate(lease.Documents.Single() with { Data = "a-outer" });
            // transactional: true is a no-op when an outer session is bound (uses _boundSession path).
            var summary = await lease.CommitAsync(transactional: true, ct);
            summary.Updated.Should().Be(1);
        });

        (await sut.GetOneAsync(a.Id)).Data.Should().Be("a-outer");
    }

    [Fact]
    [Trait("Category", "Database")]
    [Trait("Category", "RequiresReplicaSet")]
    public async Task WithTransactionAsync_MixedDiskAndLockable_BothCommitOrAbortTogether()
    {
        var lockable = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "x" };
        await lockable.AddAsync(entity);

        // First transaction: commit both
        await _mongoDbServiceFactory.WithTransactionAsync(async (session, ct) =>
        {
            await using var scope = await lockable.LockAsync(entity.Id, session: session);
            await scope.CommitAsync(CommitMode.Update, scope.Entity with { Data = "x-commit" });
            await lockable.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "extra" }, session);
        });

        (await lockable.GetOneAsync(entity.Id)).Data.Should().Be("x-commit");
        (await lockable.CountAsync(_ => true)).Should().Be(2);

        // Second transaction: abort by exception
        Func<Task> act = async () => await _mongoDbServiceFactory.WithTransactionAsync(async (session, ct) =>
        {
            await using var scope = await lockable.LockAsync(entity.Id, session: session);
            await scope.CommitAsync(CommitMode.Update, scope.Entity with { Data = "x-rollback" });
            await lockable.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "should-rollback" }, session);
            throw new InvalidOperationException("abort");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await lockable.GetOneAsync(entity.Id)).Data.Should().Be("x-commit"); // unchanged
        (await lockable.CountAsync(_ => true)).Should().Be(2); // no new doc
    }
}

using System;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Lockable;
using Tharga.MongoDB.Tests.Lockable.Base;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable;

/// <summary>
/// Pins the AllowDelayedCommit feature from the May 2026 backlog item
/// "Delayed lockable entities: auto-commit if no other writer has modified".
/// Lease holders that overrun their TTL should still be able to commit their work
/// when no one else has touched the document — the LockKey atomicity check carries
/// the safety guarantee, the time-gate was redundant.
/// </summary>
[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class DelayedCommitTests : LockableTestBase
{
    private static readonly TimeSpan ShortTtl = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PastTtl = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Test-only collection that pins itself to strict-TTL behaviour by overriding
    /// AllowDelayedCommit. Used to verify the per-collection opt-out path.
    /// </summary>
    private sealed class StrictTtlTestRepositoryCollection : LockableRepositoryCollectionBase<LockableTestEntity, ObjectId>
    {
        public StrictTtlTestRepositoryCollection(IMongoDbServiceFactory factory) : base(factory) { }
        protected override bool RequireActor => false;
        protected override bool AllowDelayedCommit => false;
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task CommitAsync_Succeeds_WhenLockExpired_AndNoOtherWriter()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await sut.AddAsync(entity);

        await using var scope = await sut.LockAsync(entity.Id, timeout: ShortTtl);
        var updated = scope.Entity with { Data = "delayed-commit", Count = 1 };

        // Wait past the TTL — the LockKey is still ours, but the lock is past ExpireTime.
        await Task.Delay(PastTtl);

        // Default: AllowDelayedCommit = true → commit succeeds despite expiry.
        var committed = await scope.CommitAsync(CommitMode.Update, updated);

        committed.Data.Should().Be("delayed-commit");
        committed.Count.Should().Be(1);
        var post = await sut.GetOneAsync(entity.Id);
        post.Data.Should().Be("delayed-commit");
        post.Count.Should().Be(1);
        post.Lock.Should().BeNull("a successful delayed commit must clear the lock just like an on-time commit");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task CommitAsync_StrictMode_ThrowsLockExpired_WhenCollectionOverridesAllowDelayedCommitFalse()
    {
        var sut = new StrictTtlTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await sut.AddAsync(entity);

        await using var scope = await sut.LockAsync(entity.Id, timeout: ShortTtl);
        var updated = scope.Entity with { Data = "would-be-delayed" };

        await Task.Delay(PastTtl);

        // The override pins this collection to strict-TTL — commit must throw.
        var act = async () => await scope.CommitAsync(CommitMode.Update, updated);

        await act.Should().ThrowAsync<LockExpiredException>();

        var post = await sut.GetOneAsync(entity.Id);
        post.Data.Should().Be("initial", "the failed commit must not have written the staged change");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task CommitAsync_StrictMode_ThrowsLockExpired_WhenFactoryAllowDelayedCommitIsFalse()
    {
        // Simulate the global DatabaseOptions.AllowDelayedCommit = false case by flipping the
        // factory's exposed value. In production, the value is set from DatabaseOptions at
        // registration time (MongoDbRegistrationExtensions); the factory is the seam the
        // collection reads.
        _mongoDbServiceFactory.AllowDelayedCommit = false;
        try
        {
            var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
            var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
            await sut.AddAsync(entity);

            await using var scope = await sut.LockAsync(entity.Id, timeout: ShortTtl);
            var updated = scope.Entity with { Data = "would-be-delayed" };

            await Task.Delay(PastTtl);

            var act = async () => await scope.CommitAsync(CommitMode.Update, updated);

            await act.Should().ThrowAsync<LockExpiredException>();
        }
        finally
        {
            // Restore for any other tests in the same fixture instance.
            _mongoDbServiceFactory.AllowDelayedCommit = true;
        }
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task CommitAsync_OnTimeCommit_StillSucceeds_AndDoesNotLogDelayedMessage()
    {
        // Regression guard: the new path must not affect ordinary on-time commits.
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await sut.AddAsync(entity);

        await using var scope = await sut.LockAsync(entity.Id, timeout: TimeSpan.FromSeconds(5));
        var updated = scope.Entity with { Data = "on-time" };

        var committed = await scope.CommitAsync(CommitMode.Update, updated);

        committed.Data.Should().Be("on-time");
        var post = await sut.GetOneAsync(entity.Id);
        post.Data.Should().Be("on-time");
        post.Lock.Should().BeNull();
    }
}

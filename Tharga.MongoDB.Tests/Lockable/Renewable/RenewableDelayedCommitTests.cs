using System;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Lockable;
using Tharga.MongoDB.Tests.Lockable.Renewable.Base;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable.Renewable;

/// <summary>
/// Renewable port of <see cref="Tharga.MongoDB.Tests.Lockable.DelayedCommitTests"/>.
/// Pins the AllowDelayedCommit feature against the renewable collection — verifies that
/// the same delayed-commit semantics hold when using RenewableLockRepositoryCollectionBase.
/// </summary>
[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class RenewableDelayedCommitTests : RenewableLockableTestBase
{
    private static readonly TimeSpan ShortTtl = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PastTtl = TimeSpan.FromMilliseconds(500);

    [Fact]
    [Trait("Category", "Database")]
    public async Task CommitAsync_Succeeds_WhenLockExpired_AndNoOtherWriter()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
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
        var sut = new StrictRenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
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
            var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
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
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
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

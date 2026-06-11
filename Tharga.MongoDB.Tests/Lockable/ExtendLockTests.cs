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
/// Integration tests for "buy more time" — extending an active lock via <c>ExtendLockAsync</c>: the
/// LockKey-guarded write pushes expiry, the per-collection write-throttle suppresses redundant writes
/// (force bypasses it), after-expiry extension follows the AllowDelayedCommit gate, and a lost lock throws.
/// </summary>
[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class ExtendLockTests : LockableTestBase
{
    private static readonly TimeSpan ShortTtl = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan PastShortTtl = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan LongTtl = TimeSpan.FromSeconds(30);

    /// <summary>No write-throttle, delayed-commit allowed (default). Every extend writes.</summary>
    private sealed class NoThrottleCollection : LockableRepositoryCollectionBase<LockableTestEntity, ObjectId>
    {
        public NoThrottleCollection(IMongoDbServiceFactory factory) : base(factory) { }
        protected override bool RequireActor => false;
        protected override TimeSpan MinLockExtendInterval => TimeSpan.Zero;
    }

    /// <summary>No write-throttle, strict TTL (no delayed commit) — an expired lock cannot be extended/committed.</summary>
    private sealed class StrictNoThrottleCollection : LockableRepositoryCollectionBase<LockableTestEntity, ObjectId>
    {
        public StrictNoThrottleCollection(IMongoDbServiceFactory factory) : base(factory) { }
        protected override bool RequireActor => false;
        protected override bool AllowDelayedCommit => false;
        protected override TimeSpan MinLockExtendInterval => TimeSpan.Zero;
    }

    /// <summary>One-second write-throttle for exercising the throttle/force behavior.</summary>
    private sealed class ThrottleCollection : LockableRepositoryCollectionBase<LockableTestEntity, ObjectId>
    {
        public ThrottleCollection(IMongoDbServiceFactory factory) : base(factory) { }
        protected override bool RequireActor => false;
        protected override TimeSpan MinLockExtendInterval => TimeSpan.FromSeconds(1);
    }

    private async Task<LockableTestEntity> Seed(LockableRepositoryCollectionBase<LockableTestEntity, ObjectId> sut)
    {
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await sut.AddAsync(entity);
        return entity;
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task ExtendLockAsync_Write_PushesExpiry_KeepsLockKey_AndPersists()
    {
        var sut = new NoThrottleCollection(_mongoDbServiceFactory);
        var entity = await Seed(sut);

        await using var scope = await sut.PickForUpdateAsync(entity.Id, timeout: ShortTtl);
        var locked = await sut.GetOneAsync(entity.Id);   // the lock lives in the DB; scope.Entity is the pre-lock image
        var originalExpiry = locked.Lock.ExpireTime;
        var lockKey = locked.Lock.LockKey;

        var before = DateTime.UtcNow;
        var result = await scope.ExtendLockAsync(LongTtl);
        var after = DateTime.UtcNow;

        result.Extended.Should().BeTrue();
        result.ExpireTime.Should().BeAfter(originalExpiry);
        result.ExpireTime.Should().BeOnOrAfter(before.Add(LongTtl).AddSeconds(-2));
        result.ExpireTime.Should().BeOnOrBefore(after.Add(LongTtl).AddSeconds(2));

        var persisted = await sut.GetOneAsync(entity.Id);
        persisted.Lock.ExpireTime.Should().BeCloseTo(result.ExpireTime, TimeSpan.FromMilliseconds(5), "the persisted document reflects the new expiry (BSON ms precision)");
        persisted.Lock.LockKey.Should().Be(lockKey, "extending keeps the same lock key");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task ExtendLockAsync_WithinThrottleWindow_IsNoOp_AndDoesNotWrite()
    {
        var sut = new ThrottleCollection(_mongoDbServiceFactory);   // 1s throttle; acquisition seeds last-write time
        var entity = await Seed(sut);

        await using var scope = await sut.PickForUpdateAsync(entity.Id, timeout: LongTtl);
        var locked = await sut.GetOneAsync(entity.Id);
        var originalExpiry = locked.Lock.ExpireTime;

        // Immediately after acquisition we are inside the 1s window → throttled no-op.
        var result = await scope.ExtendLockAsync(LongTtl);

        result.Extended.Should().BeFalse();
        result.ExpireTime.Should().BeCloseTo(originalExpiry, TimeSpan.FromMilliseconds(5));

        var persisted = await sut.GetOneAsync(entity.Id);
        persisted.Lock.ExpireTime.Should().BeCloseTo(originalExpiry, TimeSpan.FromMilliseconds(5), "a throttled extend must not write");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task ExtendLockAsync_Force_BypassesThrottle_AndWrites()
    {
        var sut = new ThrottleCollection(_mongoDbServiceFactory);
        var entity = await Seed(sut);

        await using var scope = await sut.PickForUpdateAsync(entity.Id, timeout: ShortTtl);
        var originalExpiry = (await sut.GetOneAsync(entity.Id)).Lock.ExpireTime;

        var result = await scope.ExtendLockAsync(LongTtl, force: true);

        result.Extended.Should().BeTrue();
        result.ExpireTime.Should().BeAfter(originalExpiry);
        (await sut.GetOneAsync(entity.Id)).Lock.ExpireTime.Should().BeCloseTo(result.ExpireTime, TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task ExtendLockAsync_KeepsLockAlive_SoStrictCommitSucceedsPastOriginalTtl()
    {
        var sut = new StrictNoThrottleCollection(_mongoDbServiceFactory);
        var entity = await Seed(sut);

        await using var scope = await sut.PickForUpdateAsync(entity.Id, timeout: ShortTtl);
        var result = await scope.ExtendLockAsync(LongTtl);
        result.Extended.Should().BeTrue();

        // Past the ORIGINAL short TTL — without the extension this strict commit would throw LockExpiredException.
        await Task.Delay(PastShortTtl);

        var committed = await scope.CommitAsync(scope.Entity with { Data = "extended-then-committed" });

        committed.Data.Should().Be("extended-then-committed");
        var persisted = await sut.GetOneAsync(entity.Id);
        persisted.Data.Should().Be("extended-then-committed");
        persisted.Lock.Should().BeNull("a successful commit clears the lock");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task ExtendLockAsync_AfterExpiry_Succeeds_WhenNotStolen_AndDelayedAllowed()
    {
        var sut = new NoThrottleCollection(_mongoDbServiceFactory);   // AllowDelayedCommit = true (default)
        var entity = await Seed(sut);

        await using var scope = await sut.PickForUpdateAsync(entity.Id, timeout: ShortTtl);

        // Let the lock pass its expiry; no one steals it, so the LockKey still matches.
        await Task.Delay(PastShortTtl);

        var result = await scope.ExtendLockAsync(LongTtl);

        result.Extended.Should().BeTrue("an expired-but-unstolen lock can be revived when delayed operations are allowed");
        (await sut.GetOneAsync(entity.Id)).Lock.ExpireTime.Should().BeCloseTo(result.ExpireTime, TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task ExtendLockAsync_AfterExpiry_Throws_StrictMode()
    {
        var sut = new StrictNoThrottleCollection(_mongoDbServiceFactory);
        var entity = await Seed(sut);

        var scope = await sut.PickForUpdateAsync(entity.Id, timeout: ShortTtl);
        await Task.Delay(PastShortTtl);

        // Strict TTL: an expired lock cannot be extended even though no one took it.
        var act = async () => await scope.ExtendLockAsync(LongTtl);

        await act.Should().ThrowAsync<LockExpiredException>();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task ExtendLockAsync_Throws_WhenLockWasStolenAfterExpiry()
    {
        var sut = new NoThrottleCollection(_mongoDbServiceFactory);   // delayed allowed, so we reach the LockKey guard
        var entity = await Seed(sut);

        var scope = await sut.PickForUpdateAsync(entity.Id, timeout: ShortTtl);

        // Let the lock expire, then another actor re-acquires it (new LockKey).
        await Task.Delay(PastShortTtl);
        await using var stealer = await sut.PickForUpdateAsync(entity.Id, timeout: LongTtl);

        // The original holder can no longer extend — its LockKey no longer matches the document.
        var act = async () => await scope.ExtendLockAsync(LongTtl);

        await act.Should().ThrowAsync<LockExpiredException>();
    }
}

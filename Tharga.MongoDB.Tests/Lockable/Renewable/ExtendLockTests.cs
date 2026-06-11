using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Lockable;
using Tharga.MongoDB.Lockable.Renewable;
using Tharga.MongoDB.Tests.Lockable.Renewable.Base;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable.Renewable;

/// <summary>
/// Contract tests for ExtendLockAsync / RenewableEntityScope.ExtendAsync.
/// All timing constants are intentionally generous so the suite stays green on a loaded CI machine.
/// </summary>
[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class ExtendLockTests : RenewableLockableTestBase
{
    // ---- timing constants (generous for CI) ----
    private static readonly TimeSpan ShortLease = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan PastOriginalLease = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan LongExtension = TimeSpan.FromSeconds(30);

    // ---- helpers ----
    private static LockableTestEntity NewEntity(string data = "initial") =>
        new() { Id = ObjectId.GenerateNewId(), Data = data };

    // ---------------------------------------------------------------
    // 1. ExtendAsync moves ExpireTime; strict-TTL commit still succeeds.
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Database")]
    public async Task ExtendAsync_MovesExpireTime_AndStrictCommitSucceeds()
    {
        var sut = new StrictRenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = NewEntity();
        await sut.AddAsync(entity);

        await using var scope = await sut.PickForUpdateAsync(entity.Id, timeout: ShortLease);

        var newExpiry = await scope.ExtendAsync(LongExtension);

        // The returned expiry should be well past the original lease.
        newExpiry.Should().BeAfter(DateTime.UtcNow.Add(ShortLease));

        // Verify that the in-DB ExpireTime actually moved.
        var midFlight = await sut.GetWithLockInfoAsync().FirstAsync();
        midFlight.Lock.ExpireTime.Should().BeCloseTo(newExpiry, TimeSpan.FromSeconds(2));

        // Wait past the original (un-extended) TTL.
        await Task.Delay(PastOriginalLease);

        // Strict collection: commit should succeed because the extension is still valid.
        var updated = scope.Entity with { Data = "extended-then-committed", Count = 1 };
        var committed = await scope.CommitAsync(updated);

        committed.Data.Should().Be("extended-then-committed");
        committed.Count.Should().Be(1);
        var post = await sut.GetOneAsync(entity.Id);
        post.Data.Should().Be("extended-then-committed");
        post.Lock.Should().BeNull();
    }

    // ---------------------------------------------------------------
    // 2. ExtendAsync throws LockLostException when another actor stole the lock.
    //    LockLost token must be cancelled as a side-effect.
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Database")]
    public async Task ExtendAsync_Throws_LockLost_WhenStolen()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = NewEntity();
        await sut.AddAsync(entity);

        // First actor picks with a short lease.
        var firstScope = await sut.PickForUpdateAsync(entity.Id, timeout: ShortLease);

        // Wait past TTL so the document becomes available for re-pick.
        await Task.Delay(PastOriginalLease);

        // Second actor steals the lock.
        await using var secondScope = await sut.PickForUpdateAsync(entity.Id, timeout: LongExtension);
        secondScope.Should().NotBeNull();

        // First actor tries to extend — must throw LockLostException.
        Func<Task> act = () => firstScope.ExtendAsync(LongExtension);
        await act.Should().ThrowAsync<LockLostException>();

        // LockLost token must be cancelled after a LockLostException.
        firstScope.LockLost.IsCancellationRequested.Should().BeTrue();

        await secondScope.CommitAsync(secondScope.Entity with { Data = "stolen" });
    }

    // ---------------------------------------------------------------
    // 3. ExtendAsync throws LockLostException when document was deleted externally.
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Database")]
    public async Task ExtendAsync_Throws_LockLost_WhenDocumentDeleted()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = NewEntity();
        await sut.AddAsync(entity);

        var scope = await sut.PickForUpdateAsync(entity.Id, timeout: LongExtension);

        // External delete bypasses the lock.
        await sut.DeleteManyAsync(DeleteMode.Any);

        Func<Task> act = () => scope.ExtendAsync(LongExtension);
        await act.Should().ThrowAsync<LockLostException>();
    }

    // ---------------------------------------------------------------
    // 4. ExtendAsync succeeds on a non-strict collection even when the lock has expired
    //    but has NOT been stolen (LockKey still matches).
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Database")]
    public async Task ExtendAsync_Succeeds_WhenExpiredButNotStolen()
    {
        // RenewableLockableTestRepositoryCollection is non-strict (AllowDelayedCommit = true).
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = NewEntity();
        await sut.AddAsync(entity);

        await using var scope = await sut.PickForUpdateAsync(entity.Id, timeout: ShortLease);

        // Let the lease expire without another actor touching it.
        await Task.Delay(PastOriginalLease);

        // Non-strict: extension of an expired-but-not-stolen lock must succeed.
        Func<Task> act = () => scope.ExtendAsync(LongExtension);
        await act.Should().NotThrowAsync();

        // Lock should not be lost.
        scope.LockLost.IsCancellationRequested.Should().BeFalse();

        await scope.CommitAsync(scope.Entity with { Data = "extended-after-expiry" });
    }

    // ---------------------------------------------------------------
    // 5. ExtendAsync on a STRICT collection throws LockExpiredException when the lease
    //    has expired (lock is intact — LockLost must NOT be cancelled).
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Database")]
    public async Task ExtendAsync_Throws_LockExpired_WhenExpired_OnStrictCollection()
    {
        var sut = new StrictRenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = NewEntity();
        await sut.AddAsync(entity);

        var scope = await sut.PickForUpdateAsync(entity.Id, timeout: ShortLease);

        await Task.Delay(PastOriginalLease);

        Func<Task> act = () => scope.ExtendAsync(LongExtension);
        await act.Should().ThrowAsync<LockExpiredException>();

        // LockExpiredException is NOT a lock-lost event; the token must remain un-cancelled.
        scope.LockLost.IsCancellationRequested.Should().BeFalse();
    }

    // ---------------------------------------------------------------
    // 6. ExtendAsync throws LockAlreadyReleasedException after the scope has been committed.
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Database")]
    public async Task ExtendAsync_Throws_LockAlreadyReleased_AfterCommit()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = NewEntity();
        await sut.AddAsync(entity);

        var scope = await sut.PickForUpdateAsync(entity.Id, timeout: LongExtension);
        await scope.CommitAsync(scope.Entity with { Data = "committed" });

        Func<Task> act = () => scope.ExtendAsync(LongExtension);
        await act.Should().ThrowAsync<LockAlreadyReleasedException>();
    }

    // ---------------------------------------------------------------
    // 7. ExtendAsync prevents an about-to-expire lock from being picked by another actor.
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Database")]
    public async Task ExtendAsync_PreventsExpiredPickup()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = NewEntity();
        await sut.AddAsync(entity);

        await using var scope = await sut.PickForUpdateAsync(entity.Id, timeout: ShortLease);

        // Extend before the lock expires so it will be valid for another 30 s.
        await scope.ExtendAsync(LongExtension);

        // A second actor must NOT be able to pick the (now extended) lock.
        Func<Task> act = () => sut.PickForUpdateAsync(entity.Id, timeout: ShortLease);
        await act.Should().ThrowAsync<LockException>("the lock is still valid after extension");

        await scope.CommitAsync(scope.Entity with { Data = "protected" });
    }

    // ---------------------------------------------------------------
    // 8. ExtendAsync on a RenewableLockScope (LockAsync path) works; commit succeeds after extension past original TTL.
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Database")]
    public async Task ExtendAsync_OnRenewableLockScope_Works()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = NewEntity();
        await sut.AddAsync(entity);

        await using var scope = await sut.LockAsync(entity.Id, timeout: ShortLease);

        var newExpiry = await scope.ExtendAsync(LongExtension);
        newExpiry.Should().BeAfter(DateTime.UtcNow.Add(ShortLease));

        // Wait past original TTL.
        await Task.Delay(PastOriginalLease);

        var updated = scope.Entity with { Data = "lockscope-extended" };
        var committed = await scope.CommitAsync(CommitMode.Update, updated);

        committed.Data.Should().Be("lockscope-extended");
        var post = await sut.GetOneAsync(entity.Id);
        post.Lock.Should().BeNull();
    }

    // ---------------------------------------------------------------
    // 9. ExtendAsync with zero or negative extension throws ArgumentException.
    //    This is a pure-logic guard; no Mongo round-trip needed.
    //    Assumption: ArgumentException is thrown synchronously before any DB call.
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Database")]
    public async Task ExtendAsync_NegativeOrZeroExtension_ThrowsArgumentException()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = NewEntity();
        await sut.AddAsync(entity);

        await using var scope = await sut.PickForUpdateAsync(entity.Id, timeout: LongExtension);

        Func<Task> zeroAct = () => scope.ExtendAsync(TimeSpan.Zero);
        await zeroAct.Should().ThrowAsync<ArgumentException>();

        Func<Task> negativeAct = () => scope.ExtendAsync(TimeSpan.FromSeconds(-1));
        await negativeAct.Should().ThrowAsync<ArgumentException>();

        // Scope should still be usable after the guard throws.
        await scope.CommitAsync(scope.Entity with { Data = "after-guard-throws" });
    }

    // ---------------------------------------------------------------
    // 10. Regression: renewal then external delete; commit surfaces the 'Cannot find entity' error.
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Database")]
    public async Task Renew_Then_DeleteDocument_Then_Commit_SurfacesCannotFindEntity()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = NewEntity();
        await sut.AddAsync(entity);

        var scope = await sut.PickForUpdateAsync(entity.Id, timeout: LongExtension);

        // Extend successfully while the document still exists.
        await scope.ExtendAsync(LongExtension);

        // External delete after the successful extension.
        await sut.DeleteManyAsync(DeleteMode.Any);

        // Commit must surface an error about the missing entity, not silently succeed or mask with a renewal error.
        Func<Task> act = () => scope.CommitAsync(scope.Entity with { Data = "should-fail" });
        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "the document no longer exists; commit must report the missing entity");
    }
}

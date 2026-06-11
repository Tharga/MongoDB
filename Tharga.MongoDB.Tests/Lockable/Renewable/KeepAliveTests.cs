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
/// Contract tests for StartKeepAlive / the background renewal loop.
/// All timing constants are intentionally generous so the suite stays green on a loaded CI machine.
/// </summary>
[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class KeepAliveTests : RenewableLockableTestBase
{
    // ---- timing constants (generous for CI) ----

    /// <summary>Short lease that expires quickly, forcing renewal to be meaningful.</summary>
    private static readonly TimeSpan ShortLease = TimeSpan.FromMilliseconds(250);

    /// <summary>Keep-alive interval: renew every ~80 ms (< ShortLease / 3).</summary>
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromMilliseconds(80);

    /// <summary>How long to hold the lock under keep-alive in the happy-path test (6 × lease).</summary>
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMilliseconds(1500);

    /// <summary>A generous window to allow background tasks to react after a trigger.</summary>
    private static readonly TimeSpan ReactionWindow = TimeSpan.FromMilliseconds(1000);

    /// <summary>A lock timeout long enough that it does not interfere with the test logic.</summary>
    private static readonly TimeSpan LongLease = TimeSpan.FromSeconds(30);

    // ---- helpers ----
    private static LockableTestEntity NewEntity(string data = "initial") =>
        new() { Id = ObjectId.GenerateNewId(), Data = data };

    // ---------------------------------------------------------------
    // 1. Keep-alive carries a strict-TTL commit past many lease cycles.
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Database")]
    public async Task StartKeepAlive_CarriesStrictCommitPastManyLeases()
    {
        var sut = new StrictRenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = NewEntity();
        await sut.AddAsync(entity);

        await using var scope = await sut.PickForUpdateAsync(entity.Id, timeout: ShortLease);

        var options = new LockKeepAliveOptions
        {
            Interval = KeepAliveInterval,
            Extension = ShortLease
        };
        await using var keepAlive = scope.StartKeepAlive(options);

        // Hold well past a single lease cycle.
        await Task.Delay(HoldDuration);

        // Strict collection: commit must succeed because keep-alive kept renewing.
        var updated = scope.Entity with { Data = "keep-alive-committed", Count = 99 };
        var committed = await scope.CommitAsync(updated);

        committed.Data.Should().Be("keep-alive-committed");
        committed.Count.Should().Be(99);
        var post = await sut.GetOneAsync(entity.Id);
        post.Lock.Should().BeNull();
    }

    // ---------------------------------------------------------------
    // 2. After abandon, the lock is released; ExpireTime does not advance further.
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Database")]
    public async Task StartKeepAlive_StopsOnAbandon_LockReleased()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = NewEntity();
        await sut.AddAsync(entity);

        var scope = await sut.PickForUpdateAsync(entity.Id, timeout: ShortLease);
        var options = new LockKeepAliveOptions { Interval = KeepAliveInterval, Extension = ShortLease };
        await using var keepAlive = scope.StartKeepAlive(options);

        // Let the loop tick a couple of times.
        await Task.Delay(KeepAliveInterval * 3);

        // Abandon — this stops the keep-alive loop via StopAsync.
        await scope.AbandonAsync();

        // Lock must be cleared immediately.
        var post = await sut.GetOneAsync(entity.Id);
        post.Lock.Should().BeNull("abandoning the scope must clear the lock regardless of keep-alive");

        // Give the loop a moment to prove it does NOT advance ExpireTime after release.
        await Task.Delay(KeepAliveInterval * 3);

        // The document has no lock; no further renewals can have occurred.
        var postPost = await sut.GetOneAsync(entity.Id);
        postPost.Lock.Should().BeNull();
    }

    // ---------------------------------------------------------------
    // 3. LockLost is cancelled within a few keep-alive intervals when another actor steals the lock.
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Database")]
    public async Task StartKeepAlive_CancelsLockLost_WhenStolen()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = NewEntity();
        await sut.AddAsync(entity);

        var firstScope = await sut.PickForUpdateAsync(entity.Id, timeout: ShortLease);

        Exception caughtFailure = null;
        var options = new LockKeepAliveOptions
        {
            Interval = KeepAliveInterval,
            Extension = ShortLease,
            OnRenewalFailure = ex => caughtFailure = ex
        };
        await using var keepAlive = firstScope.StartKeepAlive(options);

        // Force-release the document so another actor can pick it.
        await sut.ReleaseOneAsync(entity.Id, ReleaseMode.LockedOnly);

        // Second actor picks with a long lease so the first actor's next renewal attempt will find a mismatched LockKey.
        await using var secondScope = await sut.PickForUpdateAsync(entity.Id, timeout: LongLease);

        // Wait for the keep-alive loop to attempt a renewal and detect the mismatch.
        await Task.Delay(KeepAliveInterval * 4 + ReactionWindow);

        firstScope.LockLost.IsCancellationRequested.Should().BeTrue(
            "the keep-alive loop must cancel LockLost when a renewal finds the lock has been stolen");

        caughtFailure.Should().BeOfType<LockLostException>(
            "OnRenewalFailure must be invoked with LockLostException on a stolen lock");

        await secondScope.CommitAsync(secondScope.Entity with { Data = "stolen-and-committed" });
    }

    // ---------------------------------------------------------------
    // 4. Anti-zombie cap: keep-alive stops after MaxTotalDuration; another actor can then pick.
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Database")]
    public async Task StartKeepAlive_MaxTotalDuration_StopsRenewing_OtherActorCanPick()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = NewEntity();
        await sut.AddAsync(entity);

        await using var scope = await sut.PickForUpdateAsync(entity.Id, timeout: ShortLease);

        // Set MaxTotalDuration to just over one keep-alive interval so the cap triggers quickly.
        var cap = KeepAliveInterval * 2;
        var options = new LockKeepAliveOptions
        {
            Interval = KeepAliveInterval,
            Extension = ShortLease,
            MaxTotalDuration = cap
        };
        await using var keepAlive = scope.StartKeepAlive(options);

        // Wait for the cap to fire and for the lock to expire naturally (no more renewals).
        await Task.Delay(cap + ShortLease + ReactionWindow);

        // The lock is expired and no longer being renewed — another actor must be able to pick it.
        var secondScope = await sut.PickForUpdateAsync(entity.Id, timeout: LongLease);
        secondScope.Should().NotBeNull("once the anti-zombie cap stops renewals the lock must be acquirable again");
        await secondScope.CommitAsync(secondScope.Entity with { Data = "zombie-capped" });
    }

    // ---------------------------------------------------------------
    // 5. StartKeepAlive called a second time throws InvalidOperationException.
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Database")]
    public async Task StartKeepAlive_CalledTwice_ThrowsInvalidOperation()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = NewEntity();
        await sut.AddAsync(entity);

        await using var scope = await sut.PickForUpdateAsync(entity.Id, timeout: LongLease);

        await using var firstHandle = scope.StartKeepAlive(new LockKeepAliveOptions { Interval = KeepAliveInterval });

        Action act = () => scope.StartKeepAlive(new LockKeepAliveOptions { Interval = KeepAliveInterval });
        act.Should().Throw<InvalidOperationException>("keep-alive may only be started once per scope");

        await scope.CommitAsync(scope.Entity with { Data = "started-twice-guard" });
    }

    // ---------------------------------------------------------------
    // 6. Releasing a scope while a renewal is in-flight must not throw from either side,
    //    and the final DB state must be Lock == null.
    // ---------------------------------------------------------------
    [Fact]
    [Trait("Category", "Database")]
    public async Task Release_WhileRenewalInFlight_NeitherThrows_LockEndsNull()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = NewEntity();
        await sut.AddAsync(entity);

        // Very tight interval to maximise the chance of a race between renewal and release.
        var tightOptions = new LockKeepAliveOptions
        {
            Interval = TimeSpan.FromMilliseconds(50),
            Extension = LongLease
        };

        var scope = await sut.PickForUpdateAsync(entity.Id, timeout: LongLease);
        await using var keepAlive = scope.StartKeepAlive(tightOptions);

        // Release concurrently while the loop is busy renewing.
        Func<Task> releaseAct = () => scope.AbandonAsync();
        await releaseAct.Should().NotThrowAsync("release must never throw even when a renewal is in-flight");

        // Give the loop a moment to fully drain.
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        var post = await sut.GetOneAsync(entity.Id);
        post.Lock.Should().BeNull("the lock must be cleared after abandon regardless of concurrent renewals");
    }
}

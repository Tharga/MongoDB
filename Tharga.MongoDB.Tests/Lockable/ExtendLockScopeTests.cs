using System;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Lockable;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable;

/// <summary>
/// Mongo-free unit tests for the scope-level <c>ExtendLockAsync</c> wiring on
/// <see cref="EntityScope{T,TKey}"/> and <see cref="LockScope{T,TKey}"/> — delegation (extension + force
/// passed through), result pass-through, argument validation, and the released / not-supported guards.
/// The DB-guarded throttle and expiry behavior is covered by ExtendLockTests (Category=Database).
/// </summary>
public class ExtendLockScopeTests
{
    private static LockableTestEntity NewEntity() => new() { Id = ObjectId.GenerateNewId(), Data = "x" };

    private static LockExtensionResult Result(bool extended) => new() { ExpireTime = DateTime.UtcNow.AddMinutes(5), Extended = extended };

    // --- EntityScope ---

    [Fact]
    public async Task EntityScope_ExtendLockAsync_PassesExtensionAndForce_AndReturnsResult()
    {
        var expected = Result(extended: true);
        TimeSpan? seenExtension = null;
        bool? seenForce = null;

        var scope = new EntityScope<LockableTestEntity, ObjectId>(
            NewEntity(),
            releaseAction: (_, _, _) => Task.CompletedTask,
            extendAction: (ext, force) => { seenExtension = ext; seenForce = force; return Task.FromResult(expected); });

        var result = await scope.ExtendLockAsync(TimeSpan.FromMinutes(5), force: true);

        seenExtension.Should().Be(TimeSpan.FromMinutes(5));
        seenForce.Should().BeTrue();
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task EntityScope_ExtendLockAsync_DefaultsForceToFalse()
    {
        bool? seenForce = null;
        var scope = new EntityScope<LockableTestEntity, ObjectId>(NewEntity(), (_, _, _) => Task.CompletedTask, (_, force) => { seenForce = force; return Task.FromResult(Result(false)); });

        await scope.ExtendLockAsync(TimeSpan.FromMinutes(1));

        seenForce.Should().BeFalse();
    }

    [Fact]
    public async Task EntityScope_ExtendLockAsync_Throws_WhenExtensionNotPositive()
    {
        var scope = new EntityScope<LockableTestEntity, ObjectId>(NewEntity(), (_, _, _) => Task.CompletedTask, (_, _) => Task.FromResult(Result(true)));

        var act = async () => await scope.ExtendLockAsync(TimeSpan.Zero);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task EntityScope_ExtendLockAsync_Throws_AfterRelease()
    {
        var scope = new EntityScope<LockableTestEntity, ObjectId>(NewEntity(), (_, _, _) => Task.CompletedTask, (_, _) => Task.FromResult(Result(true)));
        await scope.AbandonAsync();

        var act = async () => await scope.ExtendLockAsync(TimeSpan.FromMinutes(1));

        await act.Should().ThrowAsync<LockAlreadyReleasedException>();
    }

    [Fact]
    public async Task EntityScope_ExtendLockAsync_Throws_WhenNotSupported()
    {
        // EntityScopeBuilder builds a scope with no extend action (not tied to a live collection lock).
        var scope = EntityScopeBuilder.Build(NewEntity(), (_, _, _) => Task.CompletedTask);

        var act = async () => await scope.ExtendLockAsync(TimeSpan.FromMinutes(1));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // --- LockScope ---

    [Fact]
    public async Task LockScope_ExtendLockAsync_PassesExtensionAndForce_AndReturnsResult()
    {
        var expected = Result(extended: true);
        TimeSpan? seenExtension = null;
        bool? seenForce = null;

        var scope = new LockScope<LockableTestEntity, ObjectId>(
            NewEntity(),
            releaseAction: (_, _, _) => Task.CompletedTask,
            extendAction: (ext, force) => { seenExtension = ext; seenForce = force; return Task.FromResult(expected); });

        var result = await scope.ExtendLockAsync(TimeSpan.FromMinutes(5), force: true);

        seenExtension.Should().Be(TimeSpan.FromMinutes(5));
        seenForce.Should().BeTrue();
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task LockScope_ExtendLockAsync_Throws_AfterRelease()
    {
        var scope = new LockScope<LockableTestEntity, ObjectId>(NewEntity(), (_, _, _) => Task.CompletedTask, (_, _) => Task.FromResult(Result(true)));
        await scope.AbandonAsync();

        var act = async () => await scope.ExtendLockAsync(TimeSpan.FromMinutes(1));

        await act.Should().ThrowAsync<LockAlreadyReleasedException>();
    }

    [Fact]
    public async Task LockScope_ExtendLockAsync_Throws_WhenNotSupported()
    {
        var scope = new LockScope<LockableTestEntity, ObjectId>(NewEntity(), (_, _, _) => Task.CompletedTask);

        var act = async () => await scope.ExtendLockAsync(TimeSpan.FromMinutes(1));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}

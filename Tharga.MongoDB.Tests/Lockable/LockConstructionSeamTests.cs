using System;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Lockable;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable;

/// <summary>
/// Covers the in-memory construction seam for lock/exception state (issue #132): a public
/// <see cref="Lock"/> constructor + <see cref="LockableEntityBaseExtensions.WithLock{T}"/>, so
/// lock-reading and error-routing code can be unit-tested without a live mongod. No database.
/// </summary>
public class LockConstructionSeamTests
{
    private record SeamTestEntity : LockableEntityBase
    {
        public int Count { get; init; }
        public string Data { get; init; }
    }

    [Fact]
    public void Lock_PublicCtor_ConstructsWithFields()
    {
        var key = Guid.NewGuid();
        var lockTime = DateTime.UtcNow;
        var expireTime = lockTime.AddMinutes(5);

        var @lock = new Lock
        {
            LockKey = key,
            LockTime = lockTime,
            ExpireTime = expireTime,
            Actor = "tester",
            ExceptionInfo = new ExceptionInfo { Type = "System.InvalidOperationException", Message = "boom" }
        };

        @lock.LockKey.Should().Be(key);
        @lock.LockTime.Should().Be(lockTime);
        @lock.ExpireTime.Should().Be(expireTime);
        @lock.Actor.Should().Be("tester");
        @lock.ExceptionInfo.Message.Should().Be("boom");
    }

    [Fact]
    public void WithLock_SetsLock_ReadableViaGetLockInfo_AndPreservesOtherFields()
    {
        var @lock = new Lock
        {
            LockKey = Guid.NewGuid(),
            LockTime = DateTime.UtcNow,
            ExpireTime = DateTime.UtcNow.AddMinutes(5),
            ExceptionInfo = new ExceptionInfo { Message = "boom" }
        };

        var entity = new SeamTestEntity { Id = ObjectId.GenerateNewId(), Data = "d", Count = 3 }
            .WithLock(@lock);

        entity.Should().BeOfType<SeamTestEntity>();
        entity.Data.Should().Be("d");
        entity.Count.Should().Be(3);

        var info = entity.GetLockInfo();
        info.Should().NotBeNull();
        info.LockKey.Should().Be(@lock.LockKey);
        info.ExceptionInfo.Message.Should().Be("boom");
    }

    [Fact]
    public void WithLock_Null_RepresentsUnlockedEntity()
    {
        var locked = new SeamTestEntity { Id = ObjectId.GenerateNewId() }
            .WithLock(new Lock { LockKey = Guid.NewGuid(), LockTime = DateTime.UtcNow, ExpireTime = DateTime.UtcNow.AddMinutes(1) });

        var unlocked = locked.WithLock(null);

        unlocked.GetLockInfo().Should().BeNull();
    }

    [Fact]
    public void WithLock_NullEntity_Throws()
    {
        SeamTestEntity entity = null;
        var act = () => entity.WithLock(new Lock { LockKey = Guid.NewGuid(), LockTime = DateTime.UtcNow, ExpireTime = DateTime.UtcNow.AddMinutes(1) });

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task EntityScopeBuilder_SetErrorStateAsync_RoutesExceptionWithoutDatabase()
    {
        // The concrete case from #132: unit-test a "generic exception -> SetErrorStateAsync" path
        // by building a scope over an in-memory entity with a captured release action.
        var entity = new SeamTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };

        SeamTestEntity releasedEntity = null;
        bool? committed = null;
        Exception routedException = null;

        var scope = EntityScopeBuilder.Build(entity, (e, commit, exception) =>
        {
            releasedEntity = e;
            committed = commit;
            routedException = exception;
            return Task.CompletedTask;
        });

        await scope.SetErrorStateAsync(new InvalidOperationException("boom"));

        releasedEntity.Should().BeSameAs(entity);
        committed.Should().BeFalse();
        routedException.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("boom");
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Lockable;
using Tharga.MongoDB.Lockable.Renewable;
using Tharga.MongoDB.Tests.Lockable.Base;
using Tharga.MongoDB.Tests.Lockable.Renewable.Base;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable.Renewable;

[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class RenewableReleaseUpdateTests : RenewableLockableTestBase
{
    [Theory]
    [MemberData(nameof(ReleaseTypes))]
    [Trait("Category", "Database")]
    public async Task ReleaseLockedEntity(ReleaseType release)
    {
        //Arrange
        var collection = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await collection.AddAsync(entity);
        var eventCount = 0;
        CallbackResult<LockableTestEntity> callbackResult = null;
        await using var sut = await collection.PickForUpdateAsync(entity.Id, completeAction: e =>
        {
            eventCount++;
            callbackResult = e;
            return Task.CompletedTask;
        });

        //Act
        var act = () => ReleaseAsync(release, sut, sut.Entity with { Count = 1 });

        //Assert
        await act.Should().NotThrowAsync();
        eventCount.Should().Be(1);
        callbackResult.Should().NotBeNull();
        callbackResult.Before.Id.Should().Be(entity.Id);
        callbackResult.After.Id.Should().Be(entity.Id);
        callbackResult.LockAction.Should().Be(release == ReleaseType.Commit ? LockAction.CommitUpdated : release == ReleaseType.SetErrorState ? LockAction.Exception : LockAction.Abandoned);
        var item = await collection.GetOneAsync(sut.Entity.Id);
        item.Should().NotBeNull();
        if (release != ReleaseType.SetErrorState) item.Lock.Should().BeNull(); else item.Lock.Should().NotBeNull();
    }

    [Theory]
    [MemberData(nameof(ReleaseTypes))]
    [Trait("Category", "Database")]
    public async Task ReleaseEntityWithExpiredLock(ReleaseType release)
    {
        //Arrange
        var collection = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await collection.AddAsync(entity);
        var eventCount = 0;
        CallbackResult<LockableTestEntity> callbackResult = null;
        await using var sut = await collection.PickForUpdateAsync(entity.Id, TimeSpan.Zero, completeAction: e =>
        {
            eventCount++;
            callbackResult = e;
            return Task.CompletedTask;
        });

        //Act
        var act = () => ReleaseAsync(release, sut, sut.Entity with { Count = 1 });

        //Assert — behaviour changed by lockable-delayed-commit: Commit now succeeds for an
        //expired-but-untouched lock (LockKey still matches). SetErrorState still throws —
        //the exception-release path stays strict per the feature spec. Abandon is unchanged.
        if (release == ReleaseType.SetErrorState)
        {
            await act.Should()
                .ThrowAsync<LockExpiredException>()
                .WithMessage($"Too late to release entity of type {nameof(LockableTestEntity)} locked by *");
            eventCount.Should().Be(0);
            callbackResult.Should().BeNull();
        }
        else
        {
            await act.Should().NotThrowAsync();
            if (release == ReleaseType.Commit)
            {
                eventCount.Should().Be(1, "the delayed-commit path still fires the completion callback");
                callbackResult.Should().NotBeNull();
                callbackResult.LockAction.Should().Be(LockAction.CommitUpdated);
            }
            else
            {
                eventCount.Should().Be(0, "Abandon does not fire the completion callback");
                callbackResult.Should().BeNull();
            }
        }
        var item = await collection.GetOneAsync(sut.Entity.Id);
        item.Should().NotBeNull();
    }

    [Theory]
    [MemberData(nameof(ReleaseTypes))]
    [Trait("Category", "Database")]
    public async Task ReleaseEntityTwice(ReleaseType release)
    {
        //Arrange
        var collection = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await collection.AddAsync(entity);
        await using var sut = await collection.PickForUpdateAsync(entity.Id);
        await ReleaseAsync(release, sut, sut.Entity with { Count = 1 });

        //Act
        var act = () => ReleaseAsync(release, sut, sut.Entity with { Count = 2 });

        //Assert
        await act.Should()
            .ThrowAsync<LockAlreadyReleasedException>()
            .WithMessage("Entity has already been released.");
        var item = await collection.GetOneAsync(sut.Entity.Id);
        item.Should().NotBeNull();
        if (release != ReleaseType.SetErrorState) item.Lock.Should().BeNull(); else item.Lock.Should().NotBeNull();
    }

    [Theory]
    [MemberData(nameof(ReleaseTypes))]
    [Trait("Category", "Database")]
    public async Task ReleasOtherEntity(ReleaseType release)
    {
        //Arrange
        var collection = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await collection.AddAsync(entity);
        await using var sut = await collection.PickForUpdateAsync(entity.Id);

        //Act
        var act = () => ReleaseAsync(release, sut, sut.Entity with { Id = ObjectId.GenerateNewId(), Count = 1 });

        //Assert
        if (release == ReleaseType.Commit)
        {
            await act.Should()
                .ThrowAsync<UnlockDifferentEntityException>()
                .WithMessage("Cannot release entity with different id. Original was '*");
        }

        var item = await collection.GetOneAsync(sut.Entity.Id);
        item.Should().NotBeNull();
    }

    [Theory]
    [MemberData(nameof(ReleaseTypes))]
    [Trait("Category", "Database")]
    public async Task ReleasEntityLockedByOtherScope(ReleaseType release)
    {
        //Arrange
        var collection = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await collection.AddAsync(entity);
        var eventCount = 0;
        CallbackResult<LockableTestEntity> callbackResult = null;
        await using var sut = await collection.PickForUpdateAsync(entity.Id, TimeSpan.Zero, completeAction: e =>
        {
            eventCount++;
            callbackResult = e;
            return Task.CompletedTask;
        });

        //Act
        var act = () => ReleaseAsync(release, sut, sut.Entity with { Count = 1 });

        //Assert — same as ReleaseEntityWithExpiredLock: the lockable-delayed-commit feature
        //means Commit now succeeds for an immediately-expired pick (LockKey still ours).
        //SetErrorState still throws; Abandon is unchanged.
        if (release == ReleaseType.SetErrorState)
        {
            await act.Should().ThrowAsync<LockExpiredException>();
            eventCount.Should().Be(0);
            callbackResult.Should().BeNull();
        }
        else
        {
            await act.Should().NotThrowAsync();
            if (release == ReleaseType.Commit)
            {
                eventCount.Should().Be(1);
                callbackResult.Should().NotBeNull();
            }
        }
        var item = await collection.GetOneAsync(sut.Entity.Id);
        item.Should().NotBeNull();
    }

    private static Task ReleaseAsync(ReleaseType release, RenewableEntityScope<LockableTestEntity, ObjectId> sut, LockableTestEntity entity)
    {
        switch (release)
        {
            case ReleaseType.Commit:
                return sut.CommitAsync(entity);
            case ReleaseType.Abandon:
                return sut.AbandonAsync();
            case ReleaseType.SetErrorState:
                return sut.SetErrorStateAsync(new Exception("Some issue."));
            default:
                throw new ArgumentOutOfRangeException(nameof(release), release, null);
        }
    }

    public static IEnumerable<object[]> ReleaseTypes => Enum.GetValues<ReleaseType>().Select(x => new object[] { x });
}

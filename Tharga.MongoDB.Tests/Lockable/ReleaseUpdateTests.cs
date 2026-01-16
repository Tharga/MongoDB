using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Lockable;
using Tharga.MongoDB.Tests.Lockable.Base;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable;

[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class ReleaseUpdateTests : LockableTestBase
{
    [Theory]
    [MemberData(nameof(ReleaseTypes))]
    [Trait("Category", "Database")]
    public async Task ReleaseLockedEntity(ReleaseType release)
    {
        //Arrange
        var collection = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
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
        var collection = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
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

        //Assert
        if (release != ReleaseType.Abandon)
        {
            await act.Should()
                .ThrowAsync<LockExpiredException>()
                .WithMessage($"Entity of type {nameof(LockableTestEntity)} was locked for *");
        }
        else
        {
            await act.Should().NotThrowAsync();
        }
        eventCount.Should().Be(0);
        callbackResult.Should().BeNull();
        var item = await collection.GetOneAsync(sut.Entity.Id);
        item.Should().NotBeNull();
    }

    [Theory]
    [MemberData(nameof(ReleaseTypes))]
    [Trait("Category", "Database")]
    public async Task ReleaseEntityTwice(ReleaseType release)
    {
        //Arrange
        var collection = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
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
        var collection = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
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
        var collection = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
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

        //Assert
        if (release != ReleaseType.Abandon)
        {
            await act.Should().ThrowAsync<LockExpiredException>();
        }
        else
        {
            await act.Should().NotThrowAsync();
        }
        eventCount.Should().Be(0);
        callbackResult.Should().BeNull();
        var item = await collection.GetOneAsync(sut.Entity.Id);
        item.Should().NotBeNull();
    }

    private static Task ReleaseAsync(ReleaseType release, EntityScope<LockableTestEntity, ObjectId> sut, LockableTestEntity entity)
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
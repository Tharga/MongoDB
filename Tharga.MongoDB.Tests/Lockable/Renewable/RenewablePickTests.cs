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
/// Tests that have the same outcome for PickForUpdate and PickForDelete — renewable variant.
/// </summary>
[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class RenewablePickTests : RenewableLockableTestBase
{
    [Theory]
    [InlineData(PickType.Update)]
    [InlineData(PickType.Delete)]
    [Trait("Category", "Database")]
    public async Task PickEntity(PickType type)
    {
        //Arrange
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);

        //Act
        await using var scope = await PickAsync(type, sut, entity.Id);

        //Assert
        scope.Entity.Should().NotBeNull();
        scope.Entity.Should().Be(entity);
        var item = await sut.GetOneAsync(entity.Id);
        item.Should().NotBeNull();
        item.Lock.Should().NotBeNull();
        item.Lock.ExceptionInfo.Should().BeNull();
    }

    [Theory]
    [InlineData(PickType.Update)]
    [InlineData(PickType.Delete)]
    [Trait("Category", "Database")]
    public async Task PickLockedEntity(PickType type)
    {
        //Arrange
        var firstActor = "some actor";
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);
        await sut.PickForUpdateAsync(entity.Id, actor: firstActor);

        //Act
        var act = () => PickAsync(type, sut, entity.Id, firstActor);

        //Assert
        await act.Should()
            .ThrowAsync<LockException>()
            .WithMessage($"Entity with id '{entity.Id}' is locked by '{firstActor}' for *.");
        var item = await sut.GetOneAsync(entity.Id);
        item.Should().NotBeNull();
        item.Lock.Should().NotBeNull();
        item.Lock.ExceptionInfo.Should().BeNull();
    }

    [Theory]
    [InlineData(PickType.Update)]
    [InlineData(PickType.Delete)]
    [Trait("Category", "Database")]
    public async Task PickedEntityWithException(PickType type)
    {
        //Arrange
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);
        await using var scope = await PickAsync(type, sut, entity.Id, "first actor");
        await scope.SetErrorStateAsync(new Exception("Some issue"));

        //Act
        var act = () => PickAsync(type, sut, entity.Id, "second actor");

        //Assert
        await act.Should()
            .ThrowAsync<LockErrorException>()
            .WithMessage($"Entity with id '{entity.Id}' has an exception attached.");
        var item = await sut.GetOneAsync(entity.Id);
        item.Should().NotBeNull();
        item.Lock.Should().NotBeNull();
        item.Lock.ExceptionInfo.Should().NotBeNull();
    }

    [Theory]
    [InlineData(PickType.Update)]
    [InlineData(PickType.Delete)]
    [Trait("Category", "Database")]
    public async Task PickedEntityThatDoesNotExist(PickType type)
    {
        //Arrange
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);

        //Act
        await using var scope = await PickAsync(type, sut, ObjectId.GenerateNewId());

        //Assert
        scope.Should().BeNull();
    }

    [Theory]
    [InlineData(PickType.Update)]
    [InlineData(PickType.Delete)]
    [Trait("Category", "Database")]
    public async Task PickEntityWithExpiredLock(PickType type)
    {
        //Arrange
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);
        await PickAsync(type, sut, entity.Id, "first actor", TimeSpan.Zero);

        //Act
        await using var result = await PickAsync(type, sut, entity.Id, "second actor");

        //Assert
        result.Entity.Should().NotBeNull();
        result.Entity.Count.Should().Be(entity.Count);
        var item = await sut.GetOneAsync(entity.Id);
        item.Should().NotBeNull();
        item.Lock.Should().NotBeNull();
        item.Lock.ExceptionInfo.Should().BeNull();
    }

    [Theory]
    [InlineData(PickType.Update)]
    [InlineData(PickType.Delete)]
    [Trait("Category", "Database")]
    public async Task PickThenAbandon(PickType type)
    {
        //Arrange
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await sut.AddAsync(entity);
        await using var scope = await PickAsync(type, sut, entity.Id);
        scope.Entity.Data = "updated";

        //Act
        await scope.AbandonAsync();

        //Assert
        var post = await sut.GetAsync(x => x.Id == entity.Id).FirstOrDefaultAsync();
        post.Data.Should().Be("initial");
        post.Lock.Should().BeNull();
        (await sut.CountAsync(x => true)).Should().Be(1);
        (await sut.GetLockedAsync(LockMode.Exception).CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(PickType.Update)]
    [InlineData(PickType.Delete)]
    [Trait("Category", "Database")]
    public async Task PickThenSetError(PickType type)
    {
        //Arrange
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await sut.AddAsync(entity);
        await using var scope = await PickAsync(type, sut, entity.Id);
        scope.Entity.Data = "updated";
        var key = await sut.GetLockedAsync(LockMode.Locked).FirstOrDefaultAsync(x => x.Entity.Id == entity.Id);

        //Act
        await scope.SetErrorStateAsync(new Exception("Some issue."));

        //Assert
        var post = await sut.GetAsync(x => x.Id == entity.Id).FirstAsync();
        post.Data.Should().Be("initial");
        post.Lock.Should().NotBeNull();
        post.Lock.LockKey.Should().Be(key.Lock.LockKey);
        post.Lock.ExceptionInfo.Message.Should().Be("Some issue.");
        post.Lock.ExceptionInfo.Type.Should().Be("Exception");
        (await sut.CountAsync(x => true)).Should().Be(1);
        (await sut.GetLockedAsync(LockMode.Exception).CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(PickType.Update)]
    [InlineData(PickType.Delete)]
    [Trait("Category", "Database")]
    public async Task PickEntityWithExpiredLockThatHaveErrors(PickType type)
    {
        ////Arrange
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);
        var scope = await sut.PickForUpdateAsync(entity.Id, actor: "first actor", timeout: TimeSpan.FromSeconds(1));
        await scope.SetErrorStateAsync(new Exception("Some issue"));
        await Task.Delay(TimeSpan.FromSeconds(1));

        //Act
        var act = () => PickAsync(type, sut, entity.Id, "second actor");

        //Assert
        await act.Should()
            .ThrowAsync<LockErrorException>()
            .WithMessage($"Entity with id '{entity.Id}' has an exception attached.");
        var item = await sut.GetOneAsync(entity.Id);
        item.Should().NotBeNull();
        item.Lock.Should().NotBeNull();
        item.Lock.ExceptionInfo.Should().NotBeNull();
    }

    [Theory]
    [InlineData(PickType.Update)]
    [InlineData(PickType.Delete)]
    [Trait("Category", "Database")]
    public async Task PickAndCommitTooLate(PickType type)
    {
        // Behaviour changed by the lockable-delayed-commit feature: when the lock is past
        // expiry but no other writer has touched the document (LockKey still matches),
        // the commit succeeds. This used to throw LockExpiredException; now it doesn't.
        // The LockKey atomicity check still drives safety — see PickAndCommitTooLateWhenOtherHavePicked
        // for the "another writer picked" case.
        //Arrange
        var firstActor = "some actor";
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await sut.AddAsync(entity);
        var timeSpan = TimeSpan.Zero;
        await using var scope = await PickAsync(type, sut, entity.Id, firstActor, timeSpan);
        var updated = scope.Entity with { Count = 1, Data = "updated" };

        //Act
        var act = () => scope.CommitAsync(updated);

        //Assert — delayed commit succeeds, document reflects the staged change.
        await act.Should().NotThrowAsync();
        var item = await sut.GetOneAsync(entity.Id);
        if (type == PickType.Delete)
        {
            item.Should().BeNull("a delayed PickForDelete commit must actually delete");
        }
        else
        {
            item.Should().NotBeNull();
            item.Data.Should().Be("updated");
            item.Count.Should().Be(1);
            item.Lock.Should().BeNull("a successful delayed commit clears the lock");
        }
    }

    [Theory]
    [InlineData(PickType.Update)]
    [InlineData(PickType.Delete)]
    [Trait("Category", "Database")]
    public async Task PickAndSetErrorTooLate(PickType type)
    {
        //Arrange
        var firstActor = "some actor";
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await sut.AddAsync(entity);
        var timeSpan = TimeSpan.Zero;
        await using var scope = await PickAsync(type, sut, entity.Id, firstActor, timeSpan);

        //Act
        var act = () => scope.SetErrorStateAsync(new Exception("some issue."));

        //Assert
        await act.Should()
            .ThrowAsync<LockExpiredException>()
            .WithMessage($"Too late to release entity of type {nameof(LockableTestEntity)} locked by *");
        var item = await sut.GetOneAsync(entity.Id);
        item.Should().NotBeNull();
        item.Lock.Should().NotBeNull();
        item.Lock.ExceptionInfo.Should().BeNull();
    }

    [Theory]
    [InlineData(PickType.Update)]
    [InlineData(PickType.Delete)]
    [Trait("Category", "Database")]
    public async Task PickAndAbandonTooLate(PickType type)
    {
        //Arrange
        var firstActor = "some actor";
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await sut.AddAsync(entity);
        var timeSpan = TimeSpan.Zero;
        await using var scope = await PickAsync(type, sut, entity.Id, firstActor, timeSpan);

        //Act
        var act = () => scope.AbandonAsync();

        //Assert
        await act.Should().NotThrowAsync();
        var item = await sut.PickForDeleteAsync(entity.Id);
        item.Should().NotBeNull();
    }

    [Theory]
    [InlineData(PickType.Update)]
    [InlineData(PickType.Delete)]
    [Trait("Category", "Database")]
    public async Task PickAndCommitTooLateThenTryToSetException(PickType type)
    {
        // Behaviour changed by lockable-delayed-commit: the "too late" commit now
        // succeeds (when no other writer has touched the doc) rather than throwing.
        // Setting an exception on the now-released scope still throws
        // LockAlreadyReleasedException — the scope-already-released guard is unchanged.
        //Arrange
        var firstActor = "some actor";
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await sut.AddAsync(entity);
        var timeSpan = TimeSpan.Zero;
        await using var scope = await PickAsync(type, sut, entity.Id, firstActor, timeSpan);
        var updated = scope.Entity with { Count = 1, Data = "updated" };
        var preAct = () => scope.CommitAsync(updated);
        await preAct.Should().NotThrowAsync("delayed commit succeeds when no other writer has touched the document");

        //Act
        var act = () => scope.SetErrorStateAsync(new Exception("some issue."));

        //Assert
        await act.Should()
            .ThrowAsync<LockAlreadyReleasedException>()
            .WithMessage("Entity has already been released.");
    }

    [Theory]
    [InlineData(PickType.Update)]
    [InlineData(PickType.Delete)]
    [Trait("Category", "Database")]
    public async Task PickAndCommitTooLateWhenOtherHavePicked(PickType type)
    {
        // The safety guarantee of lockable-delayed-commit: when ANOTHER writer has picked
        // the expired lock, the original scope's LockKey is no longer current, and the
        // delayed commit MUST fail. The exception type is no longer LockExpiredException
        // (the strict-TTL gate was dropped) — instead the Mongo-side filter mismatch on
        // LockKey surfaces as InvalidOperationException from ReleaseAsync's "Cannot find
        // entity before release" guard.
        //Arrange
        var firstActor = "some actor";
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await sut.AddAsync(entity);
        var timeSpan = TimeSpan.FromSeconds(1);
        var scope = await PickAsync(type, sut, entity.Id, firstActor, timeSpan);
        var updated = scope.Entity with { Count = 1, Data = "updated" };
        await Task.Delay(timeSpan);
        await using var otherScope = await PickAsync(type, sut, entity.Id, firstActor, TimeSpan.FromSeconds(2));
        await otherScope.CommitAsync(scope.Entity with { Count = 1, Data = "hijacked" });
        await Task.Delay(timeSpan);

        //Act
        var act = () => scope.CommitAsync(updated);

        //Assert
        await act.Should().ThrowAsync<InvalidOperationException>();

        await scope.DisposeAsync();
    }

    private static async Task<RenewableEntityScope<LockableTestEntity, ObjectId>> PickAsync(PickType type, RenewableLockableTestRepositoryCollection sut, ObjectId entityId, string actor = "some actor", TimeSpan? timeSpan = default, Func<CallbackResult<LockableTestEntity>, Task> completeAction = default)
    {
        RenewableEntityScope<LockableTestEntity, ObjectId> scope = null;
        if (type == PickType.Update)
            scope = await sut.PickForUpdateAsync(entityId, actor: actor, timeout: timeSpan, completeAction: completeAction);
        else if (type == PickType.Delete)
            scope = await sut.PickForDeleteAsync(entityId, actor: actor, timeout: timeSpan, completeAction: completeAction);
        return scope;
    }

    public enum PickType
    {
        Update,
        Delete
    }
}

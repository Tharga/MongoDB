using System;
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
public class PickForDeleteTests : LockableTestTestsBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task PickEntity()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);

        //Act
        var result = await sut.PickForDeleteAsync(entity.Id);

        //Assert
        result.Entity.Should().NotBeNull();
        result.Entity.Should().Be(entity);
        var item = await sut.GetOneAsync(entity.Id);
        item.Should().NotBeNull();
        item.Lock.Should().NotBeNull();
        item.Lock.ExceptionInfo.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task PickLockedEntity()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity
        {
            Id = ObjectId.GenerateNewId(),
            Lock = new Lock
            {
                LockKey = Guid.NewGuid(),
                LockTime = DateTime.UtcNow,
                Actor = "some actor",
            }
        };
        await sut.AddAsync(entity);

        //Act
        var act = () => sut.PickForDeleteAsync(entity.Id, actor: "test actor");

        //Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"Entity with id '{entity.Id}' is locked by 'some actor' for *.");
        var item = await sut.GetOneAsync(entity.Id);
        item.Should().NotBeNull();
        item.Lock.Should().NotBeNull();
        item.Lock.ExceptionInfo.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task PickedEntityWithException()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity
        {
            Id = ObjectId.GenerateNewId(),
            Lock = new Lock { ExceptionInfo = new ExceptionInfo() }
        };
        await sut.AddAsync(entity);

        //Act
        var act = () => sut.PickForDeleteAsync(entity.Id);

        //Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"Entity with id '{entity.Id}' has an exception attached.");
        var item = await sut.GetOneAsync(entity.Id);
        item.Should().NotBeNull();
        item.Lock.Should().NotBeNull();
        item.Lock.ExceptionInfo.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task PickedEntityThatDoesNotExist()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity
        {
            Id = ObjectId.GenerateNewId(),
        };

        //Act
        var result = await sut.PickForDeleteAsync(entity.Id);

        //Assert
        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task PickEntityWithExpiredLock()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity
        {
            Id = ObjectId.GenerateNewId(),
            Count = 1,
            Lock = new Lock { ExpireTime = DateTime.UtcNow }
        };
        await sut.AddAsync(entity);

        //Act
        var result = await sut.PickForDeleteAsync(entity.Id);

        //Assert
        result.Entity.Should().NotBeNull();
        result.Entity.Count.Should().Be(entity.Count);
        var item = await sut.GetOneAsync(entity.Id);
        item.Should().NotBeNull();
        item.Lock.Should().NotBeNull();
        item.Lock.ExceptionInfo.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task PickForDeleteCommit()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);
        var result = await sut.PickForDeleteAsync(entity.Id);

        //Act
        var r = await result.CommitAsync();

        //Assert
        r.Should().Be(entity);
        (await sut.CountAsync(x => true)).Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task PickForDeleteAbandon()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await sut.AddAsync(entity);
        var scope = await sut.PickForDeleteAsync(entity.Id);
        scope.Entity.Data = "updated";

        //Act
        await scope.AbandonAsync();

        //Assert
        var post = await sut.GetAsync(x => x.Id == entity.Id).FirstOrDefaultAsync();
        post.Data.Should().Be("initial");
        //post.UnlockCounter.Should().Be(0);
        post.Lock.Should().BeNull();
        (await sut.CountAsync(x => true)).Should().Be(1);
        (await sut.GetLockedAsync(LockMode.Exception).CountAsync()).Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task PickForDeleteException()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await sut.AddAsync(entity);
        var scope = await sut.PickForDeleteAsync(entity.Id);
        scope.Entity.Data = "updated";

        //Act
        await scope.SetErrorStateAsync(new Exception("Some issue."));

        //Assert
        var post = await sut.GetAsync(x => x.Id == entity.Id).FirstAsync();
        post.Data.Should().Be("initial");
        //post.UnlockCounter.Should().Be(0);
        post.Lock.Should().NotBeNull();
        post.Lock.ExceptionInfo.Message.Should().Be("Some issue.");
        (await sut.CountAsync(x => true)).Should().Be(1);
        (await sut.GetLockedAsync(LockMode.Exception).CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(ActionHelper.EndAction.Abandon, ActionHelper.EndAction.Abandon, false)]
    [InlineData(ActionHelper.EndAction.Abandon, ActionHelper.EndAction.Commit, false)]
    [InlineData(ActionHelper.EndAction.Abandon, ActionHelper.EndAction.Exception, false)]
    [InlineData(ActionHelper.EndAction.Commit, ActionHelper.EndAction.Abandon, true)]
    [InlineData(ActionHelper.EndAction.Commit, ActionHelper.EndAction.Commit, true)]
    [InlineData(ActionHelper.EndAction.Commit, ActionHelper.EndAction.Exception, true)]
    [InlineData(ActionHelper.EndAction.Exception, ActionHelper.EndAction.Abandon, false)]
    [InlineData(ActionHelper.EndAction.Exception, ActionHelper.EndAction.Commit, false)]
    [InlineData(ActionHelper.EndAction.Exception, ActionHelper.EndAction.Exception, false)]
    public async Task CloseTwice(ActionHelper.EndAction first, ActionHelper.EndAction then, bool deleted)
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);
        var scope = await sut.PickForDeleteAsync(entity.Id);
        var firstAct = ActionHelper.Action(first, scope);

        await firstAct.Invoke();

        //Act
        var act = ActionHelper.Action(then, scope);

        //Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Entity has already been released.");
        var item = await sut.GetOneAsync(entity.Id);
        if (deleted)
        {
            item.Should().BeNull();
        }
        else
        {
            item.Should().NotBeNull();
        }
    }
}
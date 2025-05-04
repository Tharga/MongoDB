using System;
using System.Threading.Tasks;
using FluentAssertions;
using Tharga.MongoDB.Tests.Support;
using Xunit;
using MongoDB.Bson;
using Tharga.MongoDB.Lockable;
using Tharga.MongoDB.Tests.Lockable.Base;
using System.Linq;

namespace Tharga.MongoDB.Tests.Lockable;

[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class PickForUpdateTests : LockableTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task PickForUpdateCommitSame()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await sut.AddAsync(entity);
        await using var scope = await sut.PickForUpdateAsync(entity.Id);
        scope.Entity.Data = "updated";

        //Act
        var r = await scope.CommitAsync();

        //Assert
        r.Should().Be(scope.Entity);
        r.Data.Should().Be("updated");
        (await sut.CountAsync(x => true)).Should().Be(1);
        var post = await sut.GetAsync(x => x.Id == entity.Id).FirstAsync();
        post.Data.Should().Be("updated");
        (await sut.CountAsync(x => true)).Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task PickForUpdateCommitOther()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await sut.AddAsync(entity);
        await using var scope = await sut.PickForUpdateAsync(entity.Id);
        var updated = scope.Entity with { Count = 1, Data = "updated" };

        //Act
        var r = await scope.CommitAsync(updated);

        //Assert
        r.Should().Be(updated);
        r.Data.Should().Be("updated");
        var post = await sut.GetAsync(x => x.Id == entity.Id).FirstAsync();
        post.Count.Should().Be(1);
        post.Data.Should().Be("updated");
        post.Lock.Should().BeNull();
        (await sut.CountAsync(x => true)).Should().Be(1);
        (await sut.GetLockedAsync(LockMode.Exception).CountAsync()).Should().Be(0);
    }

    //--->

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
    [Trait("Category", "Database")]
    public async Task CloseTwice(ActionHelper.EndAction first, ActionHelper.EndAction then, bool updated)
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await sut.AddAsync(entity);
        await using var scope = await sut.PickForUpdateAsync(entity.Id);
        scope.Entity.Data = "updated";
        var firstAct = ActionHelper.Action(first, scope);

        await firstAct.Invoke();

        //Act
        var act = ActionHelper.Action(then, scope);

        //Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Entity has already been released.");
        var item = await sut.GetOneAsync(entity.Id);
        item.Should().NotBeNull();
        if (updated)
        {
            item.Data.Should().Be("updated");
        }
        else
        {
            item.Data.Should().Be("initial");
        }
    }
}
using System;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Tests.Lockable.Base;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable;

[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class PickForDeleteTests : LockableTestBase
{
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
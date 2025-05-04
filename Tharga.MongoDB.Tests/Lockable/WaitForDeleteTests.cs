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
public class WaitForDeleteTests : LockableTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task WaitForDelete()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);

        //Act
        await using var result = await sut.WaitForDeleteAsync(entity.Id);

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
    public async Task WaitForLockedEntityThatIsNotReleased()
    {
        //Arrange
        var firstActor = "some actor";
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);
        await sut.PickForUpdateAsync(entity.Id, actor: firstActor);

        //Act
        var act = () => sut.WaitForDeleteAsync(entity.Id, TimeSpan.FromSeconds(1), default, "test actor");

        //Assert
        await act.Should()
            .ThrowAsync<TimeoutException>()
            .WithMessage("No valid entity has been released for update.");
        var item = await sut.GetOneAsync(entity.Id);
        item.Should().NotBeNull();
        item.Lock.Should().NotBeNull();
        item.Lock.ExceptionInfo.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task WaitForLockedEntityThatIsReleased()
    {
        //Arrange
        var firstActor = "some actor";
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);
        await sut.PickForUpdateAsync(entity.Id, actor: firstActor, timeout: TimeSpan.FromSeconds(1));

        //Act
        await using var result = await sut.WaitForDeleteAsync(entity.Id, TimeSpan.FromSeconds(2));

        //Assert
        result.Entity.Should().NotBeNull();
        var item = await sut.GetOneAsync(entity.Id);
        item.Should().NotBeNull();
        item.Lock.Should().NotBeNull();
        item.Lock.ExceptionInfo.Should().BeNull();
    }
}
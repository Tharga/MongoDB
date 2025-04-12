using System;
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
public class WaitForUpdateTests : LockableTestTestsBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task WaitForEntity()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);

        //Act
        var result = await sut.WaitForUpdateAsync(entity.Id);

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
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity
        {
            Id = ObjectId.GenerateNewId(),
            Lock = new Lock
            {
                LockKey = Guid.NewGuid(),
                LockTime = DateTime.UtcNow,
                Actor = "some actor",
                ExpireTime = DateTime.UtcNow.AddSeconds(5)
            }
        };
        await sut.AddAsync(entity);

        //Act
        var act = () => sut.WaitForUpdateAsync(entity.Id, TimeSpan.FromSeconds(1), default, "test actor");

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
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity
        {
            Id = ObjectId.GenerateNewId(),
            Lock = new Lock
            {
                LockKey = Guid.NewGuid(),
                LockTime = DateTime.UtcNow,
                Actor = "some actor",
                ExpireTime = DateTime.UtcNow.AddSeconds(1)
            }
        };
        await sut.AddAsync(entity);

        //Act
        var result = await sut.WaitForUpdateAsync(entity.Id, TimeSpan.FromSeconds(5));

        //Assert
        result.Entity.Should().NotBeNull();
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
        var act = () => sut.WaitForUpdateAsync(entity.Id);

        //Assert
        await act.Should()
            .ThrowAsync<LockErrorException>()
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
        var result = await sut.WaitForUpdateAsync(entity.Id);

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
        var result = await sut.WaitForUpdateAsync(entity.Id);

        //Assert
        result.Entity.Should().NotBeNull();
        result.Entity.Count.Should().Be(entity.Count);
        var item = await sut.GetOneAsync(entity.Id);
        item.Should().NotBeNull();
        item.Lock.Should().NotBeNull();
        item.Lock.ExceptionInfo.Should().BeNull();
    }
}
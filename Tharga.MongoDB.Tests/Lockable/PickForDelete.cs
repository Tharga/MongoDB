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
public class PickForDelete : LockableTestTestsBase
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
            Lock = new Lock
            {
                ExpireTime = DateTime.UtcNow
            }
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
}
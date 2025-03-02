using System;
using System.Threading.Tasks;
using FluentAssertions;
using Tharga.MongoDB.Tests.Support;
using Xunit;
using MongoDB.Bson;
using Tharga.MongoDB.Lockable;
using Tharga.MongoDB.Tests.Lockable.Base;

namespace Tharga.MongoDB.Tests.Lockable;

[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
//[Trait("Category", "Database")]
public class PickForUpdateTests : LockableTestTestsBase
{
    [Fact]
    public async Task PickEntity()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);

        //Act
        var result = await sut.GetForUpdateAsync(entity.Id);

        //Assert
        result.Entity.Should().NotBeNull();
        result.Entity.Should().Be(entity);
        var item = await sut.GetOneAsync(entity.Id);
        item.Should().NotBeNull();
        item.Lock.Should().NotBeNull();
        item.Lock.ExceptionInfo.Should().BeNull();
    }

    [Fact]
    public async Task PickLockedEntity()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity
        {
            Id = ObjectId.GenerateNewId(),
            Lock = new Lock()
        };
        await sut.AddAsync(entity);

        //Act
        var act = () => sut.GetForUpdateAsync(entity.Id);

        //Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"Entity with id '{entity.Id}' is locked.");
        var item = await sut.GetOneAsync(entity.Id);
        item.Should().NotBeNull();
        item.Lock.Should().NotBeNull();
        item.Lock.ExceptionInfo.Should().BeNull();
    }

    [Fact]
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
        var act = () => sut.GetForUpdateAsync(entity.Id);

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
    public async Task PickedEntityThatDoesNotExist()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity
        {
            Id = ObjectId.GenerateNewId(),
        };

        //Act
        var act = () => sut.GetForUpdateAsync(entity.Id);

        //Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"Cannot find entity with id '{entity.Id}'.");
        var item = await sut.GetOneAsync(entity.Id);
        item.Should().BeNull();
    }

    [Fact]
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
        var result = await sut.GetForUpdateAsync(entity.Id);

        //Assert
        result.Entity.Should().NotBeNull();
        result.Entity.Count.Should().Be(entity.Count);
        var item = await sut.GetOneAsync(entity.Id);
        item.Should().NotBeNull();
        item.Lock.Should().NotBeNull();
        item.Lock.ExceptionInfo.Should().BeNull();
    }

    //TODO: Wait for entity to be updated
}
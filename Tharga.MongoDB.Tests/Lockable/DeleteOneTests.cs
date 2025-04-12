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
public class DeleteOneTests : LockableTestTestsBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task DeleteOneAsync()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);

        //Act
        var result = await sut.DeleteOneAsync(entity.Id);

        //Assert
        result.Should().Be(entity);
        (await sut.CountAsync(x => true)).Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DeleteWhenLocked()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);
        await sut.PickForUpdateAsync(entity.Id, actor: "some actor");

        //Act
        var act = () => sut.DeleteOneAsync(entity.Id);

        //Assert
        await act.Should()
            .ThrowAsync<LockException>()
            .WithMessage($"Entity with id '{entity.Id}' is locked by 'some actor' for *.");
        (await sut.CountAsync(x => true)).Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DeleteWhenExpired()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);
        var scope = await sut.PickForUpdateAsync(entity.Id, actor: "some actor");
        await scope.SetErrorStateAsync(new Exception("Some issue."));

        //Act
        var act = () => sut.DeleteOneAsync(entity.Id);

        //Assert
        await act.Should()
            .ThrowAsync<LockErrorException>()
            .WithMessage($"Entity with id '{entity.Id}' has an exception attached.");
        (await sut.CountAsync(x => true)).Should().Be(1);
    }

}
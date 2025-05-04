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
public class EntityScopeExtensionsTests : LockableTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task UpdateAndAbandon()
    {
        //Arrange
        var repository = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await repository.AddAsync(entity);
        await using var scope = await repository.PickForUpdateAsync(entity.Id);

        //Act
        var result = await scope.ExecuteAsync(() =>
        {
            entity.Data = "updated";
            return Task.FromResult<LockableTestEntity>(default);
        });

        //Assert
        result.Should().BeNull();
        var after = await repository.GetOneAsync();
        after.Data.Should().Be("initial");
        after.Lock.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task UpdateAndCommit()
    {
        //Arrange
        var repository = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await repository.AddAsync(entity);
        await using var scope = await repository.PickForUpdateAsync(entity.Id);

        //Act
        var result = await scope.ExecuteAsync(() => Task.FromResult(entity with { Data = "updated" }));

        //Assert
        result.Should().NotBeNull();
        result.Data.Should().Be("updated");
        var after = await repository.GetOneAsync();
        after.Data.Should().Be("updated");
        after.Lock.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task UpdateAndThrow()
    {
        //Arrange
        var repository = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await repository.AddAsync(entity);
        await using var scope = await repository.PickForUpdateAsync(entity.Id);

        //Act
        var act = () => scope.ExecuteAsync(() =>
        {
            entity.Data = "updated";
            throw new InvalidOperationException("Oups");
        });

        //Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Oups");

        var after = await repository.GetOneAsync();
        after.Data.Should().Be("initial");
        after.Lock.ExceptionInfo.Message.Should().Be("Oups");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DeleteAndAbandon()
    {
        //Arrange
        var repository = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await repository.AddAsync(entity);
        await using var scope = await repository.PickForDeleteAsync(entity.Id);

        //Act
        var result = await scope.ExecuteAsync(() => Task.FromResult<LockableTestEntity>(default));

        //Assert
        result.Should().BeNull();
        var after = await repository.GetOneAsync();
        after.Data.Should().Be("initial");
        after.Lock.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DeleteAndCommit()
    {
        //Arrange
        var repository = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await repository.AddAsync(entity);
        await using var scope = await repository.PickForDeleteAsync(entity.Id);

        //Act
        var result = await scope.ExecuteAsync(() => Task.FromResult(entity));

        //Assert
        result.Should().NotBeNull();
        var after = await repository.GetOneAsync();
        after.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DeleteAndThrow()
    {
        //Arrange
        var repository = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await repository.AddAsync(entity);
        await using var scope = await repository.PickForDeleteAsync(entity.Id);

        //Act
        var act = () => scope.ExecuteAsync(() => throw new InvalidOperationException("Oups"));

        //Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Oups");

        var after = await repository.GetOneAsync();
        after.Data.Should().Be("initial");
        after.Lock.ExceptionInfo.Message.Should().Be("Oups");
    }
}
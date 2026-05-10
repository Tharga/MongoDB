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
        var result = await scope.ExecuteAsync(e =>
        {
            e.Data = "updated";
            return Task.FromResult<LockableTestEntity>(default);
        });

        //Assert
        result.Should().BeNull();
        var after = await repository.GetOneAsync(x => true);
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
        var result = await scope.ExecuteAsync(e => Task.FromResult(e with { Data = "updated" }));

        //Assert
        result.Should().NotBeNull();
        result.Data.Should().Be("updated");
        var after = await repository.GetOneAsync(x => true);
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
        var act = () => scope.ExecuteAsync(e =>
        {
            e.Data = "updated";
            throw new InvalidOperationException("Oups");
        });

        //Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Oups");

        var after = await repository.GetOneAsync(x => true);
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
        var result = await scope.ExecuteAsync(e => Task.FromResult<LockableTestEntity>(default));

        //Assert
        result.Should().BeNull();
        var after = await repository.GetOneAsync(x => true);
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
        var result = await scope.ExecuteAsync(Task.FromResult);

        //Assert
        result.Should().NotBeNull();
        var after = await repository.GetOneAsync(x => true);
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
        var act = () => scope.ExecuteAsync(_ => throw new InvalidOperationException("Oups"));

        //Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Oups");

        var after = await repository.GetOneAsync(x => true);
        after.Data.Should().Be("initial");
        after.Lock.ExceptionInfo.Message.Should().Be("Oups");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task UpdateAndThrow_StructuredHandlerReceivesHandlerErrorKind()
    {
        //Arrange
        var repository = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await repository.AddAsync(entity);
        await using var scope = await repository.PickForUpdateAsync(entity.Id);

        LockableErrorKind? capturedKind = null;
        Exception capturedException = null;

        //Act
        var result = await scope.ExecuteAsync(
            _ => throw new InvalidOperationException("Oups"),
            (kind, e) => { capturedKind = kind; capturedException = e; });

        //Assert — handler called once with HandlerError kind, no exception escapes the call
        capturedKind.Should().Be(LockableErrorKind.HandlerError);
        capturedException.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("Oups");
        // SetErrorStateAsync still ran — exception is recorded on the lock
        var after = await repository.GetOneAsync(x => true);
        after.Lock.ExceptionInfo.Message.Should().Be("Oups");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task UpdateAndThrow_LegacyHandlerStillReceivesException()
    {
        //Arrange
        var repository = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await repository.AddAsync(entity);
        await using var scope = await repository.PickForUpdateAsync(entity.Id);

        Exception captured = null;

        //Act — legacy Action<Exception> overload
        await scope.ExecuteAsync(
            _ => throw new InvalidOperationException("Oups"),
            (Action<Exception>)(e => captured = e));

        //Assert — exception flows to the legacy handler unchanged
        captured.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("Oups");
        var after = await repository.GetOneAsync(x => true);
        after.Lock.ExceptionInfo.Message.Should().Be("Oups");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task NullStructuredHandler_HandlerErrorPropagates()
    {
        //Arrange
        var repository = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await repository.AddAsync(entity);
        await using var scope = await repository.PickForUpdateAsync(entity.Id);

        //Act — explicitly null structured handler
        var act = () => scope.ExecuteAsync(
            _ => throw new InvalidOperationException("Oups"),
            (Action<LockableErrorKind, Exception>)null);

        //Assert — same propagation behavior as before this overload existed
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Oups");
    }
}
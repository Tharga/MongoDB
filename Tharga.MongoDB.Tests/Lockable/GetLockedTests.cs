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
public class GetLockedTests : LockableTestTestsBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task GetLockedExceptions()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var lockableTestEntity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(lockableTestEntity);
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId() });
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId() });
        var scope = await sut.PickForUpdateAsync(lockableTestEntity.Id);
        await scope.SetErrorStateAsync(new Exception("Some issue"));

        //Act
        var items = await sut.GetLockedAsync(LockMode.Exception).ToArrayAsync();

        //Assert
        items.Length.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task GetLockedExpired()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var lockableTestEntity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(lockableTestEntity);
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId() });
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId() });
        await sut.PickForUpdateAsync(lockableTestEntity.Id, TimeSpan.Zero);

        //Act
        var items = await sut.GetExpiredAsync().ToArrayAsync();

        //Assert
        items.Length.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task GetLockedLocked()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var lockableTestEntity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(lockableTestEntity);
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId() });
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId() });
        await sut.PickForUpdateAsync(lockableTestEntity.Id);

        //Act
        var items = await sut.GetLockedAsync(LockMode.Locked).ToArrayAsync();

        //Assert
        items.Length.Should().Be(1);
    }
}
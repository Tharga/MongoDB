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
public class ReleaseAsyncTests : LockableTestTestsBase
{
    [Theory]
    [Trait("Category", "Database")]
    [InlineData(ReleaseMode.ExceptionOnly, 1)]
    [InlineData(ReleaseMode.LockOnly, 0)]
    [InlineData(ReleaseMode.Any, 0)]
    [Trait("Category", "Database")]
    public async Task ReleaseLockedAsync(ReleaseMode mode, int locked)
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var lockedEntity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Lock = new Lock { LockTime = DateTime.UtcNow, LockKey = Guid.NewGuid() } };
        await sut.AddAsync(lockedEntity);

        //Act
        var result = await sut.ReleaseOneAsync(lockedEntity.Id, mode);

        //Assert
        result.Should().Be(locked == 0);
        (await sut.CountAsync(x => true)).Should().Be(1);
        (await sut.GetLockedAsync(LockMode.Locked).ToArrayAsync()).Length.Should().Be(locked);
        (await sut.GetLockedAsync(LockMode.Expired).ToArrayAsync()).Length.Should().Be(0);
        (await sut.GetLockedAsync(LockMode.Exception).ToArrayAsync()).Length.Should().Be(0);
        (await sut.GetUnlockedAsync().ToArrayAsync()).Length.Should().Be(1 - locked);
    }

    [Theory]
    [Trait("Category", "Database")]
    [InlineData(ReleaseMode.ExceptionOnly, 0)]
    [InlineData(ReleaseMode.LockOnly, 1)]
    [InlineData(ReleaseMode.Any, 0)]
    [Trait("Category", "Database")]
    public async Task ReleaseExceptionAsync(ReleaseMode mode, int locked)
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var lockedEntity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Lock = new Lock { LockTime = DateTime.UtcNow, LockKey = Guid.NewGuid(), ExceptionInfo = new ExceptionInfo { Message = "Some issue." } } };
        await sut.AddAsync(lockedEntity);

        //Act
        var result = await sut.ReleaseOneAsync(lockedEntity.Id, mode);

        //Assert
        result.Should().Be(locked == 0);
        (await sut.CountAsync(x => true)).Should().Be(1);
        (await sut.GetLockedAsync(LockMode.Locked).ToArrayAsync()).Length.Should().Be(0);
        (await sut.GetLockedAsync(LockMode.Expired).ToArrayAsync()).Length.Should().Be(0);
        (await sut.GetLockedAsync(LockMode.Exception).ToArrayAsync()).Length.Should().Be(locked);
        (await sut.GetUnlockedAsync().ToArrayAsync()).Length.Should().Be(1 - locked);
    }

    [Theory]
    [Trait("Category", "Database")]
    [InlineData(ReleaseMode.ExceptionOnly)]
    [InlineData(ReleaseMode.LockOnly)]
    [InlineData(ReleaseMode.Any)]
    [Trait("Category", "Database")]
    public async Task ReleaseTimeoutAsync(ReleaseMode mode)
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var lockedEntity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Lock = new Lock { LockTime = DateTime.UtcNow, LockKey = Guid.NewGuid(), ExpireTime = DateTime.UtcNow } };
        await sut.AddAsync(lockedEntity);
        await Task.Delay(500);

        //Act
        var result = await sut.ReleaseOneAsync(lockedEntity.Id, mode);

        //Assert
        result.Should().Be(mode != ReleaseMode.ExceptionOnly);
        (await sut.CountAsync(x => true)).Should().Be(1);
        (await sut.GetLockedAsync(LockMode.Locked).ToArrayAsync()).Length.Should().Be(0);
        (await sut.GetLockedAsync(LockMode.Expired).ToArrayAsync()).Length.Should().Be(mode != ReleaseMode.ExceptionOnly ? 0 : 1);
        (await sut.GetLockedAsync(LockMode.Exception).ToArrayAsync()).Length.Should().Be(0);
        (await sut.GetUnlockedAsync().ToArrayAsync()).Length.Should().Be(1);
    }

    [Theory]
    [Trait("Category", "Database")]
    [InlineData(ReleaseMode.ExceptionOnly)]
    [InlineData(ReleaseMode.LockOnly)]
    [InlineData(ReleaseMode.Any)]
    [Trait("Category", "Database")]
    public async Task ReleaseUnlockedAsync(ReleaseMode mode)
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var lockedEntity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(lockedEntity);

        //Act
        var result = await sut.ReleaseOneAsync(lockedEntity.Id, mode);

        //Assert
        result.Should().BeFalse();
        (await sut.CountAsync(x => true)).Should().Be(1);
        (await sut.GetLockedAsync(LockMode.Locked).ToArrayAsync()).Length.Should().Be(0);
        (await sut.GetLockedAsync(LockMode.Expired).ToArrayAsync()).Length.Should().Be(0);
        (await sut.GetLockedAsync(LockMode.Exception).ToArrayAsync()).Length.Should().Be(0);
        (await sut.GetUnlockedAsync().ToArrayAsync()).Length.Should().Be(1);
    }
}
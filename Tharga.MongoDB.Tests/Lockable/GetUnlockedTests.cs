using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Tests.Lockable.Base;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable;

[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class GetUnlockedTests : LockableTestTestsBase
{
    [Fact]
    public async Task Basic()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var lockableTestEntity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        var entityWithLock = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entityWithLock);
        await sut.PickForUpdateAsync(entityWithLock.Id);

        var entityEithException = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entityEithException);
        var scope = await sut.PickForUpdateAsync(entityEithException.Id);
        await scope.SetErrorStateAsync(new Exception("Some issue"));

        var entityThatTimedOut = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entityThatTimedOut);
        await sut.PickForUpdateAsync(entityThatTimedOut.Id, TimeSpan.Zero);

        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId() });
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId() });

        //Act
        var items = await sut.GetUnlockedAsync().ToArrayAsync();

        //Assert
        items.Length.Should().Be(3);
    }
}
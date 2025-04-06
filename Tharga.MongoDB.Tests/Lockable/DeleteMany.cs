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
public class DeleteMany : LockableTestTestsBase
{
    [Theory]
    [Trait("Category", "Database")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DeleteManyAsync(int count)
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        for (var i = 0; i < count; i++)
        {
            await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId() });
        }

        //Act
        var result = await sut.DeleteManyAsync(x => true);

        //Assert
        result.Should().Be(count);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DeleteWhenOneIsLocked()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var generateNewId = ObjectId.GenerateNewId();
        await sut.AddAsync(new LockableTestEntity { Id = generateNewId });
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId() });
        await sut.PickForUpdateAsync(generateNewId);

        //Act
        var result = await sut.DeleteManyAsync(x => true);

        //Assert
        result.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DeleteWhenOneIsExpired()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var generateNewId = ObjectId.GenerateNewId();
        await sut.AddAsync(new LockableTestEntity { Id = generateNewId });
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId() });
        await sut.PickForUpdateAsync(generateNewId, TimeSpan.Zero);

        //Act
        var result = await sut.DeleteManyAsync(x => true);

        //Assert
        result.Should().Be(2);
    }
}
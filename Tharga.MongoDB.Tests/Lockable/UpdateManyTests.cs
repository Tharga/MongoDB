using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Tests.Lockable.Base;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable;

public class UpdateManyTests : LockableTestBase
{
    [Theory]
    [Trait("Category", "Database")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task UpdateManyAsync(int count)
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        for (var i = 0; i < count; i++)
        {
            await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId() });
        }

        var filter = new FilterDefinitionBuilder<LockableTestEntity>().Empty;
        var update = new UpdateDefinitionBuilder<LockableTestEntity>().Set(x => x.Count, 1);

        //Act
        var result = await sut.UpdateUnlockedAsync(filter, update);

        //Assert
        result.Should().Be(count);
        var stored = await sut.GetAsync(x => true).ToArrayAsync();
        stored.Sum(x => x.Count).Should().Be(count);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task UpdateWhenOneIsLocked()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var generateNewId = ObjectId.GenerateNewId();
        await sut.AddAsync(new LockableTestEntity { Id = generateNewId });
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId() });
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId() });
        await using var scope = await sut.PickForUpdateAsync(generateNewId);

        var filter = new FilterDefinitionBuilder<LockableTestEntity>().Empty;
        var update = new UpdateDefinitionBuilder<LockableTestEntity>().Set(x => x.Count, 1);

        //Act
        var result = await sut.UpdateUnlockedAsync(filter, update);

        //Assert
        result.Should().Be(2);
        var stored = await sut.GetAsync(x => true).ToArrayAsync();
        stored.Sum(x => x.Count).Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task UpdateWhenOneIsExpired()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var generateNewId = ObjectId.GenerateNewId();
        await sut.AddAsync(new LockableTestEntity { Id = generateNewId });
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId() });
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId() });
        await using var scope = await sut.PickForUpdateAsync(generateNewId, TimeSpan.Zero);

        var filter = new FilterDefinitionBuilder<LockableTestEntity>().Empty;
        var update = new UpdateDefinitionBuilder<LockableTestEntity>().Set(x => x.Count, 1);

        //Act
        var result = await sut.UpdateUnlockedAsync(filter, update);

        //Assert
        result.Should().Be(3);
        var stored = await sut.GetAsync(x => true).ToArrayAsync();
        stored.Sum(x => x.Count).Should().Be(3);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task UpdateWhenOneHasError()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var generateNewId = ObjectId.GenerateNewId();
        await sut.AddAsync(new LockableTestEntity { Id = generateNewId });
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId() });
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId() });
        await using var scope = await sut.PickForUpdateAsync(generateNewId, TimeSpan.FromSeconds(1));
        await scope.SetErrorStateAsync(new Exception("some issue"));
        await Task.Delay(TimeSpan.FromSeconds(1));

        var filter = new FilterDefinitionBuilder<LockableTestEntity>().Empty;
        var update = new UpdateDefinitionBuilder<LockableTestEntity>().Set(x => x.Count, 1);

        //Act
        var result = await sut.UpdateUnlockedAsync(filter, update);

        //Assert
        result.Should().Be(2);
        var stored = await sut.GetAsync(x => true).ToArrayAsync();
        stored.Sum(x => x.Count).Should().Be(2);
    }
}
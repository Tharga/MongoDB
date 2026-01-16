using Bogus;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Linq;
using System.Threading.Tasks;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class UpdateOneAsyncTest : GenericRepositoryCollectionBaseTestBase
{
    public UpdateOneAsyncTest()
    {
        Prepare([TestEntityFactory.CreateTestEntity, TestEntityFactory.CreateTestEntity]);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task BasicWithFilter()
    {
        //Arrange
        var sut = await GetCollection();
        var filter = new FilterDefinitionBuilder<TestEntity>().Eq(x => x.Id, InitialData.First().Id);
        var updatedValue = new Faker().Random.String2(20);
        var update = new UpdateDefinitionBuilder<TestEntity>().Set(x => x.Value, updatedValue);

        //Act
        var result = await sut.UpdateOneAsync(filter, update);

        //Assert
        result.Before.Should().Be(InitialData.First());
        (await sut.GetAsync(x => x.Id == InitialData.First().Id).ToArrayAsync()).First().Value.Should().Be(updatedValue);
        await VerifyContentAsync(sut);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task BasicWithPredicate()
    {
        //Arrange
        var sut = await GetCollection();
        var updatedValue = new Faker().Random.String2(20);
        var update = new UpdateDefinitionBuilder<TestEntity>().Set(x => x.Value, updatedValue);

        //Act
        var result = await sut.UpdateOneAsync(x => x.Id == InitialData.First().Id, update);

        //Assert
        result.Before.Should().Be(InitialData.First());
        (await sut.GetAsync(x => x.Id == InitialData.First().Id).ToArrayAsync()).First().Value.Should().Be(updatedValue);
        await VerifyContentAsync(sut);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task MissingWithFilter()
    {
        //Arrange
        var filter = new FilterDefinitionBuilder<TestEntity>().Eq(x => x.Id, ObjectId.GenerateNewId());
        var updatedValue = new Faker().Random.String2(20);
        var update = new UpdateDefinitionBuilder<TestEntity>().Set(x => x.Value, updatedValue);
        var sut = await GetCollection();

        //Act
        var result = await sut.UpdateOneAsync(filter, update);

        //Assert
        result.Before.Should().BeNull();
        await VerifyContentAsync(sut);
    }

    [Fact(Skip = "Unique index test does not work.")]
    [Trait("Category", "Database")]
    public async Task FailedWithFilter()
    {
        //Arrange
        var sut = await GetCollection();
        var filter = new FilterDefinitionBuilder<TestEntity>().Eq(x => x.Id, InitialData.First().Id);
        var update = new UpdateDefinitionBuilder<TestEntity>().Set(x => x.Value, InitialData.Last().Value);

        //Act
        var act = () => sut.UpdateOneAsync(filter, update, OneOption<TestEntity>.Single);

        //Assert
        await act.Should().ThrowAsync<MongoCommandException>();
        await VerifyContentAsync(sut);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task BasicWithId()
    {
        //Arrange
        var updatedValue = new Faker().Random.String2(20);
        var update = new UpdateDefinitionBuilder<TestEntity>().Set(x => x.Value, updatedValue);
        var sut = await GetCollection();

        //Act
        var result = await sut.UpdateOneAsync(InitialData.First().Id, update);

        //Assert
        result.Before.Should().Be(InitialData.First());
        (await sut.GetAsync(x => x.Id == InitialData.First().Id).ToArrayAsync()).First().Value.Should().Be(updatedValue);
        await VerifyContentAsync(sut);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task MissingWithId()
    {
        //Arrange
        var updatedValue = new Faker().Random.String2(20);
        var update = new UpdateDefinitionBuilder<TestEntity>().Set(x => x.Value, updatedValue);
        var sut = await GetCollection();

        //Act
        var result = await sut.UpdateOneAsync(ObjectId.GenerateNewId(), update);

        //Assert
        result.Before.Should().BeNull();
        await VerifyContentAsync(sut);
    }

    [Fact(Skip = "Unique index test does not work.")]
    [Trait("Category", "Database")]
    public async Task FailedWithId()
    {
        //Arrange
        var sut = await GetCollection();
        var update = new UpdateDefinitionBuilder<TestEntity>().Set(x => x.Value, InitialData.Last().Value);

        //Act
        var act = () => sut.UpdateOneAsync(InitialData.First().Id, update);

        //Assert
        await act.Should().ThrowAsync<MongoCommandException>();
        await VerifyContentAsync(sut);
    }
}
using System.Linq;
using System.Threading.Tasks;
using Bogus;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class UpdateOneAsyncTest : GenericBufferRepositoryCollectionBaseTestBase
{
    public UpdateOneAsyncTest()
    {
        Prepare([TestEntityFactory.CreateTestEntity, TestEntityFactory.CreateTestEntity]);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task BasicWithFilter(CollectionType collectionType)
    {
        //Arrange
        var sut = await GetCollection(collectionType);
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

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task MissingWithFilter(CollectionType collectionType)
    {
        //Arrange
        var filter = new FilterDefinitionBuilder<TestEntity>().Eq(x => x.Id, ObjectId.GenerateNewId());
        var updatedValue = new Faker().Random.String2(20);
        var update = new UpdateDefinitionBuilder<TestEntity>().Set(x => x.Value, updatedValue);
        var sut = await GetCollection(collectionType);

        //Act
        var result = await sut.UpdateOneAsync(filter, update);

        //Assert
        result.Before.Should().BeNull();
        await VerifyContentAsync(sut);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task FailedWithFilter(CollectionType collectionType)
    {
        //Arrange
        var sut = await GetCollection(collectionType);
        var filter = new FilterDefinitionBuilder<TestEntity>().Eq(x => x.Id, InitialData.First().Id);
        var update = new UpdateDefinitionBuilder<TestEntity>().Set(x => x.Value, InitialData.Last().Value);

        //Act
        var act = () => sut.UpdateOneAsync(filter, update);

        //Assert
        await act.Should().ThrowAsync<MongoCommandException>();
        await VerifyContentAsync(sut);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task BasicWithId(CollectionType collectionType)
    {
        //Arrange
        var updatedValue = new Faker().Random.String2(20);
        var update = new UpdateDefinitionBuilder<TestEntity>().Set(x => x.Value, updatedValue);
        var sut = await GetCollection(collectionType);

        //Act
        var result = await sut.UpdateOneAsync(InitialData.First().Id, update);

        //Assert
        result.Before.Should().Be(InitialData.First());
        (await sut.GetAsync(x => x.Id == InitialData.First().Id).ToArrayAsync()).First().Value.Should().Be(updatedValue);
        await VerifyContentAsync(sut);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task MissingWithId(CollectionType collectionType)
    {
        //Arrange
        var updatedValue = new Faker().Random.String2(20);
        var update = new UpdateDefinitionBuilder<TestEntity>().Set(x => x.Value, updatedValue);
        var sut = await GetCollection(collectionType);

        //Act
        var result = await sut.UpdateOneAsync(ObjectId.GenerateNewId(), update);

        //Assert
        result.Before.Should().BeNull();
        await VerifyContentAsync(sut);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task FailedWithId(CollectionType collectionType)
    {
        //Arrange
        var sut = await GetCollection(collectionType);
        var update = new UpdateDefinitionBuilder<TestEntity>().Set(x => x.Value, InitialData.Last().Value);

        //Act
        var act = () => sut.UpdateOneAsync(InitialData.First().Id, update);

        //Assert
        await act.Should().ThrowAsync<MongoCommandException>();
        await VerifyContentAsync(sut);
    }
}
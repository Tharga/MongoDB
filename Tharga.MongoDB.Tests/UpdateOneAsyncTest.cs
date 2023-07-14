using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class UpdateOneAsyncTest : GenericBufferRepositoryCollectionBaseTestBase
{
    public UpdateOneAsyncTest()
    {
        Prepare(new[]
        {
            new Fixture().Build<TestEntity>().With(x => x.Id, ObjectId.GenerateNewId()).Create(),
        });
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task BasicWithFilter(CollectionType collectionType)
    {
        //Arrange
        var filter = new FilterDefinitionBuilder<TestEntity>().Eq(x => x.Id, InitialData.First().Id);
        var updatedValue = new Fixture().Create<string>();
        var update = new UpdateDefinitionBuilder<TestEntity>().Set(x => x.Value, updatedValue);
        var sut = await GetCollection(collectionType);

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
    public async Task BasicWithId(CollectionType collectionType)
    {
        //Arrange
        var updatedValue = new Fixture().Create<string>();
        var update = new UpdateDefinitionBuilder<TestEntity>().Set(x => x.Value, updatedValue);
        var sut = await GetCollection(collectionType);

        //Act
        var result = await sut.UpdateOneAsync(InitialData.First().Id, update);

        //Assert
        result.Before.Should().Be(InitialData.First());
        (await sut.GetAsync(x => x.Id == InitialData.First().Id).ToArrayAsync()).First().Value.Should().Be(updatedValue);
        await VerifyContentAsync(sut);
    }
}
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
public class AddOrReplaceAsync : GenericBufferRepositoryCollectionBaseTestBase
{
    public AddOrReplaceAsync()
    {
        Prepare([TestEntityFactory.CreateTestEntity]);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task BasicUpdate(CollectionType collectionType)
    {
        //Arrange
        var sut = await GetCollection(collectionType);
        var newEntity = TestEntityFactory.CreateTestEntity(InitialData.First().Id);

        //Act
        var result = await sut.AddOrReplaceAsync(newEntity);

        //Assert
        result.Before.Should().Be(InitialData.First());
        (await sut.GetAsync(x => x.Id == newEntity.Id).ToArrayAsync()).First().Should().Be(newEntity);
        await VerifyContentAsync(sut);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task BasicAdd(CollectionType collectionType)
    {
        //Arrange
        var id = ObjectId.GenerateNewId();
        var newEntity = new Faker<TestEntity>().RuleFor(x => x.Id, id).Generate();
        var sut = await GetCollection(collectionType);

        //Act
        var result = await sut.AddOrReplaceAsync(newEntity);

        //Assert
        result.Before.Should().BeNull();
        (await sut.GetAsync(x => x.Id == newEntity.Id).ToArrayAsync()).First().Should().Be(newEntity);
        await VerifyContentAsync(sut);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task FailedToAdd(CollectionType collectionType)
    {
        //Arrange
        var sut = await GetCollection(collectionType);
        var id = ObjectId.GenerateNewId();
        var newEntity = new Faker<TestEntity>()
            .RuleFor(x => x.Id, id)
            .RuleFor(x => x.Value, InitialData.First().Value)
            .Generate();

        //Act
        var act = () => sut.AddOrReplaceAsync(newEntity);

        //Assert
        await act.Should().ThrowAsync<MongoWriteException>();
        (await sut.GetAsync(x => x.Id == newEntity.Id).ToArrayAsync()).Should().BeEmpty();
        await VerifyContentAsync(sut);
    }
}
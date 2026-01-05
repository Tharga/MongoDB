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
public class AddOrReplaceAsync : GenericRepositoryCollectionBaseTestBase
{
    public AddOrReplaceAsync()
    {
        Prepare([TestEntityFactory.CreateTestEntity]);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task BasicUpdate()
    {
        //Arrange
        var sut = await GetCollection();
        var newEntity = TestEntityFactory.CreateTestEntity(InitialData.First().Id);

        //Act
        var result = await sut.AddOrReplaceAsync(newEntity);

        //Assert
        result.Before.Should().Be(InitialData.First());
        (await sut.GetAsync(x => x.Id == newEntity.Id).ToArrayAsync()).First().Should().Be(newEntity);
        await VerifyContentAsync(sut);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task BasicAdd()
    {
        //Arrange
        var id = ObjectId.GenerateNewId();
        var newEntity = new Faker<TestEntity>().RuleFor(x => x.Id, id).Generate();
        var sut = await GetCollection();

        //Act
        var result = await sut.AddOrReplaceAsync(newEntity);

        //Assert
        result.Before.Should().BeNull();
        (await sut.GetAsync(x => x.Id == newEntity.Id).ToArrayAsync()).First().Should().Be(newEntity);
        await VerifyContentAsync(sut);
    }

    [Fact(Skip = "Unique index test does not work.")]
    [Trait("Category", "Database")]
    public async Task FailedToAdd()
    {
        //Arrange
        var sut = await GetCollection();
        var id = ObjectId.GenerateNewId();
        var newEntity = new Faker<TestEntity>()
            .RuleFor(x => x.Id, id)
            .RuleFor(x => x.Value, InitialData.First().Value) //NOTE: Value with duplicate indexed value.
            .Generate();

        //Act
        var act = () => sut.AddOrReplaceAsync(newEntity);

        //Assert
        await act.Should().ThrowAsync<MongoWriteException>();
        (await sut.GetAsync(x => x.Id == newEntity.Id).ToArrayAsync()).Should().BeEmpty();
        await VerifyContentAsync(sut);
    }
}
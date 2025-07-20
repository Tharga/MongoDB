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
public class AddAsyncTest : GenericRepositoryCollectionBaseTestBase
{
    public AddAsyncTest()
    {
        Prepare([TestEntityFactory.CreateTestEntity]);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task Basic()
    {
        //Arrange
        var id = ObjectId.GenerateNewId();
        var newEntity = new Faker<TestEntity>().RuleFor(x => x.Id, id).Generate();
        var sut = await GetCollection();

        //Act
        await sut.AddAsync(newEntity);

        //Assert
        (await sut.GetAsync(x => x.Id == id).ToArrayAsync()).Should().HaveCount(1);
        await VerifyContentAsync(sut);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task AddAsync()
    {
        //Arrange
        var id = ObjectId.GenerateNewId();
        var newEntity = new Faker<TestEntity>().RuleFor(x => x.Id, id).Generate();
        var sut = await GetCollection();

        //Act
        await sut.AddAsync(newEntity);

        //Assert
        (await sut.GetAsync(x => x.Id == id).ToArrayAsync()).Should().HaveCount(1);
        await VerifyContentAsync(sut);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task AddFailed()
    {
        //Arrange
        var sut = await GetCollection();

        //Act
        var act = () => sut.AddAsync(InitialData.First());

        //Assert
        await act.Should().ThrowAsync<MongoWriteException>();
        (await sut.GetAsync(x => x.Id == InitialData.First().Id).ToArrayAsync()).Should().HaveCount(1);
        await VerifyContentAsync(sut);
    }
}
using System.Linq;
using System.Threading.Tasks;
using Bogus;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class AddManyAsyncTest : GenericRepositoryCollectionBaseTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task Basic()
    {
        //Arrange
        var id = ObjectId.GenerateNewId();
        var newEntity = new Faker<TestEntity>().RuleFor(x => x.Id, id).Generate();
        var sut = await GetCollection();

        //Act
        await sut.AddManyAsync([newEntity]);

        //Assert
        (await sut.GetAsync(x => x.Id == id).ToArrayAsync()).Should().HaveCount(1);
        await VerifyContentAsync(sut);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task FailAddingDuplicates()
    {
        //Arrange
        var id = ObjectId.GenerateNewId();
        var newEntity = new Faker<TestEntity>().RuleFor(x => x.Id, id).Generate();
        var sut = await GetCollection();

        //Act
        var act = () => sut.AddManyAsync([newEntity, newEntity]);

        //Assert
        await act.Should().ThrowAsync<MongoBulkWriteException>();
        await VerifyContentAsync(sut);
    }
}
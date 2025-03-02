using System.Linq;
using System.Threading.Tasks;
using Bogus;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class UpdateAsyncTest : GenericBufferRepositoryCollectionBaseTestBase
{
    public UpdateAsyncTest()
    {
        Prepare([TestEntityFactory.CreateTestSubEntity, TestEntityFactory.CreateTestSubEntity]);
    }

    [Fact(Skip = "Fix")]
    [Trait("Category", "Database")]
    public async Task BasicForDisk()
    {
        //Arrange
        var filter = new FilterDefinitionBuilder<TestEntity>().Empty;
        var updatedValue = new Faker<string>().Generate();
        var update = new UpdateDefinitionBuilder<TestEntity>().Set(x => x.ExtraValue, updatedValue);
        var sut = await GetCollection(CollectionType.Disk) as DiskRepositoryCollectionBase<TestEntity, ObjectId>;

        //Act
        var result = await sut.UpdateOneAsync(filter, update);

        //Assert
        result.Should().Be(InitialDataLoader.Length);
        var data = await sut.GetAsync(x => true).ToArrayAsync();
        data.All(x => x.ExtraValue == updatedValue).Should().BeTrue();
    }
}
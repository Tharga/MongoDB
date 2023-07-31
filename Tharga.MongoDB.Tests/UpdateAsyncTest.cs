using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class UpdateAsyncTest : GenericBufferRepositoryCollectionBaseTestBase
{
    public UpdateAsyncTest()
    {
        Prepare(new[]
        {
            new Fixture().Build<TestSubEntity>().With(x => x.Id, ObjectId.GenerateNewId()).Create(),
            new Fixture().Build<TestSubEntity>().With(x => x.Id, ObjectId.GenerateNewId()).Create(),
        });
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task BasicForDisk()
    {
        //Arrange
        var filter = new FilterDefinitionBuilder<TestEntity>().Empty;
        var updatedValue = new Fixture().Create<string>();
        var update = new UpdateDefinitionBuilder<TestEntity>().Set(x => x.ExtraValue, updatedValue);
        var sut = await GetCollection(CollectionType.Disk);

        //Act
        var result = await sut.UpdateAsync(filter, update);

        //Assert
        result.Should().Be(InitialData.Length);
        var data = await sut.GetAsync(x => true).ToArrayAsync();
        data.All(x => x.ExtraValue == updatedValue).Should().BeTrue();
    }
}
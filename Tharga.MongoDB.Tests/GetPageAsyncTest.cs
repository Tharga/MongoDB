using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class GetPageAsyncTest : GenericBufferRepositoryCollectionBaseTestBase
{
    public GetPageAsyncTest()
    {
        Prepare(new[]
        {
            new Fixture().Build<TestEntity>().With(x => x.Id, ObjectId.GenerateNewId()).Create(),
            new Fixture().Build<TestSubEntity>().With(x => x.Id, ObjectId.GenerateNewId()).Create(),
            new Fixture().Build<TestEntity>().With(x => x.Id, ObjectId.GenerateNewId()).Create()
        });
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task BasicFromDisk()
    {
        //Arrange
        var sut = await GetCollection(CollectionType.Disk);

        //Act
        var result = await sut.GetPageAsync(x => true).SelectMany(x => x.Items).ToArrayAsync();

        //Assert
        result.Should().NotBeNull();
        result.Length.Should().Be(3);
        await VerifyContentAsync(sut);
    }

    [Fact(Skip = "Implement")]
    [Trait("Category", "Database")]
    public async Task BasicFromBuffer()
    {
        //Arrange
        var sut = await GetCollection(CollectionType.Buffer);

        //Act
        var act = () => sut.GetPageAsync(x => true);

        //Assert
        act.Should().Throw<MongoBulkWriteException>();
        await VerifyContentAsync(sut);
    }
}
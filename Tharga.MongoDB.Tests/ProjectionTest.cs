using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class ProjectionTest : GenericBufferRepositoryCollectionBaseTestBase
{
    public ProjectionTest()
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
    public async Task GetOneProjectionAsyncTest()
    {
        //Arrange
        var sut = await GetCollection(CollectionType.Disk);

        //Act
        var result = await sut.GetOneProjectionAsync<TestProjectionEntity>(x => true, OneOption<TestProjectionEntity>.FirstOrDefault);

        //Assert
        result.Should().NotBeNull();
        result.Value.Should().Be(InitialData.First().Value);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task GetProjectionAsyncTest()
    {
        //Arrange
        var sut = await GetCollection(CollectionType.Disk);

        //Act
        var result = await sut.GetProjectionAsync<TestProjectionEntity>(x => true).ToArrayAsync();

        //Assert
        result.Should().NotBeEmpty();
        result.Should().HaveCount(InitialData.Length);
        result.First().Value.Should().Be(InitialData.First().Value);
        result.Last().Value.Should().Be(InitialData.Last().Value);
    }
}
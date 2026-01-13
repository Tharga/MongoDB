using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class ProjectionTest : GenericRepositoryCollectionBaseTestBase
{
    public ProjectionTest()
    {
        Prepare([TestEntityFactory.CreateTestEntity, TestEntityFactory.CreateTestSubEntity, TestEntityFactory.CreateTestEntity]);
    }

    [Fact(Skip = "Implement this feature.")]
    [Trait("Category", "Database")]
    public async Task GetOneProjectionAsyncTest()
    {
        //Arrange
        var sut = await GetCollection();

        //Act
        //var result = await sut.GetOneProjectionAsync(x => true, OneOption<TestProjectionEntity>.FirstOrDefault);

        ////Assert
        //result.Should().NotBeNull();
        //result.Value.Should().Be(InitialData.First().Value);
    }

    [Fact(Skip = "Implement this feature.")]
    [Trait("Category", "Database")]
    public async Task GetProjectionAsyncTest()
    {
        //Arrange
        var sut = await GetCollection();

        //Act
        var result = await sut.GetProjectionAsync<TestProjectionEntity>(x => true).ToArrayAsync();

        //Assert
        result.Should().NotBeEmpty();
        result.Should().HaveCount(InitialData.Length);
        result.First().Value.Should().Be(InitialData.First().Value);
        result.Last().Value.Should().Be(InitialData.Last().Value);
    }
}
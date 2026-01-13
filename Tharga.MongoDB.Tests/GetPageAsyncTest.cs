using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class GetPageAsyncTest : GenericRepositoryCollectionBaseTestBase
{
    public GetPageAsyncTest()
    {
        //Prepare(TestEntityFactory.CreateMany(6));
        Prepare([TestEntityFactory.CreateTestEntity, TestEntityFactory.CreateTestEntity, TestEntityFactory.CreateTestEntity, TestEntityFactory.CreateTestEntity, TestEntityFactory.CreateTestEntity, TestEntityFactory.CreateTestEntity]);
    }

    //[Fact(Skip = "Deprecated.")]
    //[Trait("Category", "Database")]
    //public async Task BasicFromDisk()
    //{
    //    //Arrange
    //    var sut = await GetCollection();
    //    sut.ResultLimit.Should().BeLessThan(InitialDataLoader.Length);
    //    InitialDataLoader.Length.Should().Be((int)await sut.CountAsync(x => true));
    //    sut.ResultLimit.Should().Be(5);

    //    //Act
    //    var result = await sut.GetPagesAsync(x => true).ToArrayAsync();

    //    //Assert
    //    result.Should().NotBeNull();
    //    result.Length.Should().Be(2);
    //    foreach (var page in result)
    //    {
    //        (await page.Items.ToArrayAsync()).Length.Should().BeLessThanOrEqualTo(5);
    //    }
    //}

    //[Fact(Skip = "Deprecated.")]
    //[Trait("Category", "Database")]
    //public async Task BasicFromDiskShouldReturnAllITems()
    //{
    //    //Arrange
    //    var sut = await GetCollection();
    //    sut.ResultLimit.Should().BeLessThan(InitialDataLoader.Length);
    //    InitialDataLoader.Length.Should().Be((int)await sut.CountAsync(x => true));

    //    //Act
    //    var result = await sut.GetPagesAsync(x => true).SelectMany(x => x.Items).ToArrayAsync();

    //    //Assert
    //    result.Should().NotBeNull();
    //    result.Length.Should().Be(InitialDataLoader.Length);
    //}

    //[Fact(Skip = "Deprecated.")]
    //[Trait("Category", "Database")]
    //public async Task GetTooManyRecordsShouldThrow()
    //{
    //    //Arrange
    //    var sut = await GetCollection();
    //    sut.ResultLimit.Should().BeLessThan(InitialDataLoader.Length);
    //    InitialDataLoader.Length.Should().Be((int)await sut.CountAsync(x => true));

    //    //Act
    //    var act = async () => await sut.GetAsync(x => true).ToArrayAsync();

    //    //Assert
    //    await act.Should().ThrowAsync<ResultLimitException>();
    //}
}
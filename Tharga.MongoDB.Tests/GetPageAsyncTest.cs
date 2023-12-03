using System;
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
public class GetPageAsyncTest : GenericBufferRepositoryCollectionBaseTestBase
{
    public GetPageAsyncTest()
    {
        Prepare(Enumerable.Range(0, 6).Select(_ => new Fixture().Build<TestEntity>().With(x => x.Id, ObjectId.GenerateNewId()).Create()));
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task BasicFromDisk()
    {
        //Arrange
        var sut = await GetCollection(CollectionType.Disk);
        sut.ResultLimit.Should().BeLessThan(InitialData.Length);
        InitialData.Length.Should().Be((int)await sut.CountAsync(x => true));
        sut.ResultLimit.Should().Be(5);

        //Act
        var result = await sut.GetPagesAsync(x => true).ToArrayAsync();

        //Assert
        result.Should().NotBeNull();
        result.Length.Should().Be(2);
        foreach (var page in result)
        {
            (await page.Items.ToArrayAsync()).Length.Should().BeLessOrEqualTo(5);
        }
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task BasicFromDiskShouldReturnAllITems()
    {
        //Arrange
        var sut = await GetCollection(CollectionType.Disk);
        sut.ResultLimit.Should().BeLessThan(InitialData.Length);
        InitialData.Length.Should().Be((int)await sut.CountAsync(x => true));

        //Act
        var result = await sut.GetPagesAsync(x => true).SelectMany(x => x.Items).ToArrayAsync();

        //Assert
        result.Should().NotBeNull();
        result.Length.Should().Be(InitialData.Length);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task GetTooManyRecordsShouldThrow()
    {
        //Arrange
        var sut = await GetCollection(CollectionType.Disk);
        sut.ResultLimit.Should().BeLessThan(InitialData.Length);
        InitialData.Length.Should().Be((int)await sut.CountAsync(x => true));

        //Act
        var act = async () => await sut.GetAsync(x => true).ToArrayAsync();

        //Assert
        await act.Should().ThrowAsync<ResultLimitException>();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task BasicFromBufferShouldThrow()
    {
        //Arrange
        var sut = await GetCollection(CollectionType.Buffer);

        //Act
        var act = async () => await sut.GetPagesAsync(x => true).ToArrayAsync();

        //Assert
        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
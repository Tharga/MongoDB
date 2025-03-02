using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Driver;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class GetOneAsyncTest : GenericBufferRepositoryCollectionBaseTestBase
{
    public GetOneAsyncTest()
    {
        Prepare([TestEntityFactory.CreateTestEntity, TestEntityFactory.CreateTestSubEntity, TestEntityFactory.CreateTestEntity]);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task Basic(CollectionType collectionType)
    {
        //Arrange
        var sut = await GetCollection(collectionType);

        //Act
        var result = await sut.GetOneAsync(x => true, OneOption<TestEntity>.First);

        //Assert
        result.Should().NotBeNull();
        await VerifyContentAsync(sut);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task Default(CollectionType collectionType)
    {
        //Arrange
        var sut = await GetCollection(collectionType);

        //Act
        var result = await sut.GetOneAsync(options: OneOption<TestEntity>.First);

        //Assert
        result.Should().NotBeNull();
        await VerifyContentAsync(sut);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task ByType(CollectionType collectionType)
    {
        //Arrange
        var sut = await GetCollection(collectionType);

        //Act
        var result = await sut.GetOneAsync<TestEntity>(options: OneOption<TestEntity>.First);

        //Assert
        result.Should().NotBeNull();
        result.GetType().Should().Be(typeof(TestEntity));
        await VerifyContentAsync(sut);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task SubType(CollectionType collectionType)
    {
        //Arrange
        var sut = await GetCollection(collectionType);

        //Act
        var result = await sut.GetOneAsync<TestSubEntity>();

        //Assert
        result.Should().NotBeNull();
        result.GetType().Should().Be(typeof(TestSubEntity));
        await VerifyContentAsync(sut);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DiskOrderAsending()
    {
        //Arrange
        var sut = await GetCollection(CollectionType.Disk);

        //Act
        var result = await sut.GetOneAsync(null, new OneOption<TestEntity> { Sort = new SortDefinitionBuilder<TestEntity>().Ascending(x => x.Id), Mode = EMode.First });

        //Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(InitialData.First().Id);
        await VerifyContentAsync(sut);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DiskOrderDescending()
    {
        //Arrange
        var sut = await GetCollection(CollectionType.Disk);

        //Act
        var result = await sut.GetOneAsync(null, new OneOption<TestEntity> { Sort = new SortDefinitionBuilder<TestEntity>().Descending(x => x.Id), Mode = EMode.First });

        //Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(InitialData.Last().Id);
        await VerifyContentAsync(sut);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task GetSingleWithMultipeResult(CollectionType collectionType)
    {
        //Arrange
        var sut = await GetCollection(collectionType);

        //Act
        var act = () => sut.GetOneAsync();

        //Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Sequence contains more than one element");
        await VerifyContentAsync(sut);
    }
}
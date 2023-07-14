using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class AddAsyncTest : GenericBufferRepositoryCollectionBaseTestBase
{
    public AddAsyncTest()
    {
        Prepare(new[]
        {
            new Fixture().Build<TestEntity>().With(x => x.Id, ObjectId.GenerateNewId()).Create(),
        });
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task Basic(CollectionType collectionType)
    {
        //Arrange
        var id = ObjectId.GenerateNewId();
        var newEntity = new Fixture().Build<TestEntity>().With(x => x.Id, id).Create();
        var sut = await GetCollection(collectionType);

        //Act
        var result = await sut.AddAsync(newEntity);

        //Assert
        result.Should().BeTrue();
        (await sut.GetAsync(x => x.Id == id).ToArrayAsync()).Should().HaveCount(1);
        await VerifyContentAsync(sut);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task AddAsync()
    {
        //Arrange
        var id = ObjectId.GenerateNewId();
        var newEntity = new Fixture().Build<TestEntity>().With(x => x.Id, id).Create();
        var sut = await GetCollection(CollectionType.Disk);

        //Act
        var result = await sut.AddAsync(newEntity);

        //Assert
        result.Should().BeTrue();
        (await sut.GetAsync(x => x.Id == id).ToArrayAsync()).Should().HaveCount(1);
        await VerifyContentAsync(sut);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task AddFailed(CollectionType collectionType)
    {
        //Arrange
        var sut = await GetCollection(collectionType);

        //Act
        var result = await sut.AddAsync(InitialData.First());

        //Assert
        result.Should().BeFalse();
        (await sut.GetAsync(x => x.Id == InitialData.First().Id).ToArrayAsync()).Should().HaveCount(1);
        await VerifyContentAsync(sut);
    }
}
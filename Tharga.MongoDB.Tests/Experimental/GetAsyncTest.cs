using AutoFixture;
using MongoDB.Bson;
using Tharga.MongoDB.Tests.Support;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using MongoDB.Driver;

namespace Tharga.MongoDB.Tests.Experimental;

public class GetAsyncTest : ExperimentalRepositoryCollectionBaseTestBase
{
    public GetAsyncTest()
    {
        Prepare(new[]
        {
            new Fixture().Build<TestEntity>().With(x => x.Id, ObjectId.GenerateNewId()).Create(),
            new Fixture().Build<TestSubEntity>().With(x => x.Id, ObjectId.GenerateNewId()).Create(),
            new Fixture().Build<TestEntity>().With(x => x.Id, ObjectId.GenerateNewId()).Create()
        });
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(AllCollectionTypes))]
    public async Task Basic(CollectionType collectionType)
    {
        //Arrange
        var sut = await GetCollection(collectionType);

        //Act
        var result = await sut.GetAsync(x => true).ToArrayAsync();

        //Assert
        result.Should().NotBeNull();
        result.Length.Should().Be(3);
        await VerifyContentAsync(sut);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(AllCollectionTypes))]
    public async Task Default(CollectionType collectionType)
    {
        //Arrange
        var sut = await GetCollection(collectionType);

        //Act
        var result = await sut.GetAsync().ToArrayAsync();

        //Assert
        result.Should().NotBeNull();
        result.Length.Should().Be(3);
        await VerifyContentAsync(sut);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(DiskCollectionTypes))]
    public async Task BasicWithFilterFromDisk(CollectionType collectionType)
    {
        //Arrange
        var sut = await GetCollection(collectionType) as IReadOnlyDiskRepositoryCollection<TestEntity, ObjectId>;

        //Act
        var filter = Builders<TestEntity>.Filter.Empty;
        var result = await sut.GetAsync(filter).ToArrayAsync();

        //Assert
        result.Should().NotBeNull();
        result.Length.Should().Be(3);
    }
}
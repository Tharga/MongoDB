using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class AddManyAsyncTest : GenericBufferRepositoryCollectionBaseTestBase
{
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
        await sut.AddManyAsync(new[] { newEntity });

        //Assert
        (await sut.GetAsync(x => x.Id == id).ToArrayAsync()).Should().HaveCount(1);
        await VerifyContentAsync(sut);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task FailAddingDuplicates(CollectionType collectionType)
    {
        //Arrange
        var id = ObjectId.GenerateNewId();
        var newEntity = new Fixture().Build<TestEntity>().With(x => x.Id, id).Create();
        var sut = await GetCollection(collectionType);

        //Act
        var act = () => sut.AddManyAsync(new[] { newEntity, newEntity });

        //Assert
        await act.Should().ThrowAsync<MongoBulkWriteException>();
        await VerifyContentAsync(sut);
    }
}
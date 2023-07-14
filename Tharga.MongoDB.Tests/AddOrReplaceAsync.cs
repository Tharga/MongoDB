using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class AddOrReplaceAsync : GenericBufferRepositoryCollectionBaseTestBase
{
    public AddOrReplaceAsync()
    {
        Prepare(new[]
        {
            new Fixture().Build<TestEntity>().With(x => x.Id, ObjectId.GenerateNewId()).Create(),
        });
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task BasicUpdate(CollectionType collectionType)
    {
        //Arrange
        var id = ObjectId.GenerateNewId();
        var newEntity = new Fixture().Build<TestEntity>().With(x => x.Id, InitialData.First().Id).Create();
        var sut = await GetCollection(collectionType);

        //Act
        var result = await sut.AddOrReplaceAsync(newEntity);

        //Assert
        result.Before.Should().Be(InitialData.First());
        (await sut.GetAsync(x => x.Id == newEntity.Id).ToArrayAsync()).First().Should().Be(newEntity);
        await VerifyContentAsync(sut);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(Data))]
    public async Task BasicAdd(CollectionType collectionType)
    {
        //Arrange
        var id = ObjectId.GenerateNewId();
        var newEntity = new Fixture().Build<TestEntity>().With(x => x.Id, id).Create();
        var sut = await GetCollection(collectionType);

        //Act
        var result = await sut.AddOrReplaceAsync(newEntity);

        //Assert
        result.Before.Should().BeNull();
        (await sut.GetAsync(x => x.Id == newEntity.Id).ToArrayAsync()).First().Should().Be(newEntity);
        await VerifyContentAsync(sut);
    }
}
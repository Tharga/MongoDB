using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using AutoFixture;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class GetAsyncTest : GenericBufferRepositoryCollectionBaseTestBase
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
    [MemberData(nameof(Data))]
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
    [MemberData(nameof(Data))]
    public async Task BasicWithFilter(CollectionType collectionType)
    {
        if (collectionType == CollectionType.Buffer) return;

        //Arrange
        var sut = await GetCollection(collectionType);

        //Act
        var filter = Builders<TestEntity>.Filter.Empty;
        var result = await sut.GetAsync(filter).ToArrayAsync();

        //Assert
        result.Should().NotBeNull();
        result.Length.Should().Be(3);
        await VerifyContentAsync(sut);
    }
}
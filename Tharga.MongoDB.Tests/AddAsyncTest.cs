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

    //[Theory(Skip = "skip")]
    //[MemberData(nameof(Data))]
    //public async Task ColdStart(CollectionType collectionType)
    //{
    //    //Arrange
    //    var id = Guid.NewGuid().ToString();
    //    var newEntity = Mock.Of<TestEntity>(x => x.Id == id && x.Value == Guid.NewGuid().ToString());
    //    var sut = await GetCollection(collectionType, async x =>
    //    {
    //        var testEntity = Mock.Of<TestEntity>(z => z.Id == Guid.NewGuid().ToString());
    //        var xxx = await x.BaseCollection.AddOrReplaceAsync(testEntity);
    //        Debug.WriteLine(await xxx.Before.Value);
    //    });

    //    //Act
    //    var result = await sut.AddAsync(newEntity);

    //    //Assert
    //    result.Should().BeTrue();
    //    (await sut.GetAsync(x => x.Id == id).ToArrayAsync()).Should().HaveCount(1);
    //    await VerifyContentAsync(sut);
    //}

    //[Theory(Skip = "skip")]
    //[MemberData(nameof(Data))]
    //public async Task AddAreadyExisting(CollectionType collectionType)
    //{
    //    //Arrange
    //    var id = Guid.NewGuid().ToString();
    //    var newEntity = Mock.Of<TestEntity>(x => x.Id == id && x.Value == Guid.NewGuid().ToString());
    //    var sut = await GetCollection(collectionType);
    //    await sut.AddAsync(Mock.Of<TestEntity>(x => x.Id == id));

    //    //Act
    //    var result = await sut.AddAsync(newEntity);

    //    //Assert
    //    result.Should().BeFalse();
    //    (await sut.GetAsync(x => x.Id == id).ToArrayAsync()).Should().HaveCount(1);
    //    await VerifyContentAsync(sut);
    //}
}
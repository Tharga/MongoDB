﻿using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Driver;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class GetAsyncTest : GenericBufferRepositoryCollectionBaseTestBase
{
    public GetAsyncTest()
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
        var result = await sut.GetAsync(x => true).ToArrayAsync();

        //Assert
        result.Should().NotBeNull();
        result.Length.Should().Be(3);
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
        var result = await sut.GetAsync().ToArrayAsync();

        //Assert
        result.Should().NotBeNull();
        result.Length.Should().Be(3);
        await VerifyContentAsync(sut);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task BasicWithFilterFromDisk()
    {
        //Arrange
        var sut = await GetCollection(CollectionType.Disk);

        //Act
        var filter = Builders<TestEntity>.Filter.Empty;
        var result = await sut.GetAsync(filter).ToArrayAsync();

        //Assert
        result.Should().NotBeNull();
        result.Length.Should().Be(3);
        await VerifyContentAsync(sut);
    }

    [Fact(Skip = "Implement")]
    [Trait("Category", "Database")]
    public async Task BasicWithFilterFromBuffer()
    {
        //Arrange
        var sut = await GetCollection(CollectionType.Buffer);

        //Act
        var filter = Builders<TestEntity>.Filter.Empty;
        var act = () => sut.GetAsync(filter);

        //Assert
        throw new NotImplementedException();
        //await act.Should().ThrowAsync<MongoBulkWriteException>();
        //await VerifyContentAsync(sut);
    }
}
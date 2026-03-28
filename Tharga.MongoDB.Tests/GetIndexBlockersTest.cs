using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bogus;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

[Collection("Sequential")]
public class GetIndexBlockersTest : MongoDbTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task WithExplicitName_FindsDuplicates()
    {
        // Arrange — collection with named unique index on Value
        var sut = new DiskTestRepositoryCollection(MongoDbServiceFactory, DatabaseContext);
        var value = new Faker().Random.AlphaNumeric(10);
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = value });

        // Insert a duplicate bypassing the unique index constraint
        var fetchResult = await sut.FetchCollectionAsync();
        var mongoCollection = fetchResult.Value;
        await mongoCollection.Indexes.DropAllAsync();
        await mongoCollection.InsertOneAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = value });

        // Act
        var blockers = await sut.GetIndexBlockers(mongoCollection, nameof(TestEntity.Value));

        // Assert
        blockers.Should().HaveCount(1);
        blockers.First().Should().HaveCount(2);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task WithoutExplicitName_FindsDuplicates()
    {
        // Arrange — collection with unnamed unique index on Value
        var sut = new UnnamedIndexTestCollection(MongoDbServiceFactory, DatabaseContext);

        // Fetch raw collection and create the unnamed index in MongoDB
        var fetchResult = await sut.FetchCollectionAsync(initiate: false);
        var mongoCollection = fetchResult.Value;
        await mongoCollection.Indexes.CreateOneAsync(
            new CreateIndexModel<TestEntity>(
                Builders<TestEntity>.IndexKeys.Ascending(f => f.Value),
                new CreateIndexOptions { Unique = false }));

        // Insert duplicate documents
        var value = new Faker().Random.AlphaNumeric(10);
        await mongoCollection.InsertOneAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = value });
        await mongoCollection.InsertOneAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = value });

        // The MongoDB-generated index name for Ascending("Value") is "Value_1"
        var blockers = await sut.GetIndexBlockers(mongoCollection, "Value_1");

        // Assert
        blockers.Should().HaveCount(1);
        blockers.First().Should().HaveCount(2);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task NoDuplicates_ReturnsEmpty()
    {
        var sut = new DiskTestRepositoryCollection(MongoDbServiceFactory, DatabaseContext);
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "unique1" });
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "unique2" });

        var fetchResult = await sut.FetchCollectionAsync();
        var mongoCollection = fetchResult.Value;

        var blockers = await sut.GetIndexBlockers(mongoCollection, nameof(TestEntity.Value));

        blockers.Should().BeEmpty();
    }

    private class UnnamedIndexTestCollection : DiskRepositoryCollectionBase<TestEntity, ObjectId>
    {
        public UnnamedIndexTestCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext)
            : base(mongoDbServiceFactory, null, databaseContext)
        {
        }

        public override string CollectionName => "UnnamedIndexTest";

        public override IEnumerable<CreateIndexModel<TestEntity>> Indices =>
        [
            new(Builders<TestEntity>.IndexKeys.Ascending(f => f.Value), new CreateIndexOptions { Unique = true })
        ];
    }
}

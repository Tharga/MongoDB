using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

[Collection("Sequential")]
public class AggregationTests : MongoDbTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task EstimatedCountAsync_EmptyCollection_ReturnsZero()
    {
        var sut = new DiskTestRepositoryCollection(MongoDbServiceFactory, DatabaseContext);

        var count = await sut.EstimatedCountAsync();

        count.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task EstimatedCountAsync_WithDocuments_ReturnsEstimate()
    {
        var sut = new DiskTestRepositoryCollection(MongoDbServiceFactory, DatabaseContext);
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "a" });
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "b" });
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "c" });

        var count = await sut.EstimatedCountAsync();

        count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task SumAsync_ReturnsServerSideSum()
    {
        var sut = new NumericTestCollection(MongoDbServiceFactory, DatabaseContext);
        await sut.AddAsync(new NumericEntity { Id = ObjectId.GenerateNewId(), Amount = 10m });
        await sut.AddAsync(new NumericEntity { Id = ObjectId.GenerateNewId(), Amount = 20m });
        await sut.AddAsync(new NumericEntity { Id = ObjectId.GenerateNewId(), Amount = 30m });

        var result = await sut.SumAsync(x => x.Amount);

        result.Should().Be(60m);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task SumAsync_WithPredicate_FiltersBeforeSum()
    {
        var sut = new NumericTestCollection(MongoDbServiceFactory, DatabaseContext);
        await sut.AddAsync(new NumericEntity { Id = ObjectId.GenerateNewId(), Amount = 10m, Category = "A" });
        await sut.AddAsync(new NumericEntity { Id = ObjectId.GenerateNewId(), Amount = 20m, Category = "B" });
        await sut.AddAsync(new NumericEntity { Id = ObjectId.GenerateNewId(), Amount = 30m, Category = "A" });

        var result = await sut.SumAsync(x => x.Amount, x => x.Category == "A");

        result.Should().Be(40m);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task AvgAsync_ReturnsServerSideAverage()
    {
        var sut = new NumericTestCollection(MongoDbServiceFactory, DatabaseContext);
        await sut.AddAsync(new NumericEntity { Id = ObjectId.GenerateNewId(), Amount = 10m });
        await sut.AddAsync(new NumericEntity { Id = ObjectId.GenerateNewId(), Amount = 20m });
        await sut.AddAsync(new NumericEntity { Id = ObjectId.GenerateNewId(), Amount = 30m });

        var result = await sut.AvgAsync(x => x.Amount);

        result.Should().Be(20m);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task MinAsync_ReturnsServerSideMin()
    {
        var sut = new NumericTestCollection(MongoDbServiceFactory, DatabaseContext);
        await sut.AddAsync(new NumericEntity { Id = ObjectId.GenerateNewId(), Amount = 10m });
        await sut.AddAsync(new NumericEntity { Id = ObjectId.GenerateNewId(), Amount = 5m });
        await sut.AddAsync(new NumericEntity { Id = ObjectId.GenerateNewId(), Amount = 30m });

        var result = await sut.MinAsync<decimal>(x => x.Amount);

        result.Should().Be(5m);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task MaxAsync_ReturnsServerSideMax()
    {
        var sut = new NumericTestCollection(MongoDbServiceFactory, DatabaseContext);
        await sut.AddAsync(new NumericEntity { Id = ObjectId.GenerateNewId(), Amount = 10m });
        await sut.AddAsync(new NumericEntity { Id = ObjectId.GenerateNewId(), Amount = 5m });
        await sut.AddAsync(new NumericEntity { Id = ObjectId.GenerateNewId(), Amount = 30m });

        var result = await sut.MaxAsync<decimal>(x => x.Amount);

        result.Should().Be(30m);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task SumAsync_EmptyCollection_ReturnsZero()
    {
        var sut = new NumericTestCollection(MongoDbServiceFactory, DatabaseContext);

        var result = await sut.SumAsync(x => x.Amount);

        result.Should().Be(0m);
    }

    private record NumericEntity : EntityBase<ObjectId>
    {
        public decimal Amount { get; init; }
        public string Category { get; init; }
    }

    private class NumericTestCollection : DiskRepositoryCollectionBase<NumericEntity, ObjectId>
    {
        public NumericTestCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext)
            : base(mongoDbServiceFactory, null, databaseContext)
        {
        }

        public override string CollectionName => "NumericTest";
        public override IEnumerable<CreateIndexModel<NumericEntity>> Indices => [];
    }
}

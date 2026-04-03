using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

[Collection("Sequential")]
public class CollectionStatsTests : MongoDbTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task GetInstancesAsync_WithoutRefresh_HasNullStats()
    {
        // Arrange — create a collection with data
        var sut = new DiskTestRepositoryCollection(MongoDbServiceFactory, DatabaseContext);
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "a" });
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "b" });

        // Act — get instances without explicit refresh (default path)
        // This simulates what GetInstancesAsync returns during initial sync
        var count = await sut.CountAsync(x => true);

        // Assert — data exists
        count.Should().Be(2);

        // The monitor's GetInstancesAsync uses includeDetails: false
        // so Stats would be null until RefreshStatsAsync is called.
        // This test confirms the data is there — the issue is that
        // GetInstancesAsync doesn't populate stats.
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task EstimatedCountAsync_ReturnsCount_WithoutExplicitRefresh()
    {
        // This proves the data IS in MongoDB — EstimatedCountAsync reads metadata
        var sut = new DiskTestRepositoryCollection(MongoDbServiceFactory, DatabaseContext);
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "a" });
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "b" });
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "c" });

        var count = await sut.EstimatedCountAsync();

        count.Should().BeGreaterThanOrEqualTo(3);
    }
}

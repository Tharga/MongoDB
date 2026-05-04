using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Paging;

[Collection("Sequential")]
public class IndexPlanTests : MongoDbTestBase
{
    private readonly PagingTestRepositoryCollection _sut;

    public IndexPlanTests()
    {
        _sut = new PagingTestRepositoryCollection(MongoDbServiceFactory, DatabaseContext);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task KeysetQuery_AgainstCompoundIndex_UsesIndexScan()
    {
        // Seed enough docs that COLLSCAN would be measurably worse — and so the planner has a real choice.
        for (var i = 1; i <= 200; i++)
        {
            await _sut.AddAsync(new PagingTestEntity
            {
                Id = ObjectId.GenerateNewId(),
                Name = $"doc-{i:D3}",
                Bucket = (i - 1) / 10,
            });
        }

        // Trigger index assurance via any read so the {Name, _id} index is created.
        await _sut.GetSizeAsync();

        // Build a representative keyset query: {Name: {$gt: "doc-050"}} sorted by {Name: 1, _id: 1}, limit 10.
        // This is exactly the shape GetPageAsync(After(...), sortBy: e => e.Name) renders.
        var explainJson = await _sut.ExecuteAsync<string>(async (collection, ct) =>
        {
            var command = new BsonDocument
            {
                {
                    "explain", new BsonDocument
                    {
                        { "find", collection.CollectionNamespace.CollectionName },
                        { "filter", new BsonDocument("Name", new BsonDocument("$gt", "doc-050")) },
                        { "sort", new BsonDocument { { "Name", 1 }, { "_id", 1 } } },
                        { "limit", 10 },
                    }
                },
                { "verbosity", "executionStats" },
            };
            var doc = await collection.Database.RunCommandAsync<BsonDocument>(command, cancellationToken: ct);
            return doc.ToString();
        }, Operation.Read, default);

        // The winning plan should not be a full collection scan.
        explainJson.Should().NotContain("\"COLLSCAN\"");
        explainJson.Should().Contain("IXSCAN");
    }
}

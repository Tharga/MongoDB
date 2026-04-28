using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class ExecuteManyAsyncTest : GenericRepositoryCollectionBaseTestBase
{
    public ExecuteManyAsyncTest()
    {
        Prepare(Enumerable.Range(0, 10).Select<int, Func<TestEntity>>(_ => TestEntityFactory.CreateTestEntity).ToArray());
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task FindWithProjectionStreamsAll()
    {
        var sut = await GetCollection();

        var projection = Builders<TestEntity>.Projection.Expression(x => new { x.Id, x.Value });
        var results = new List<string>();
        await foreach (var item in sut.ExecuteManyAsync(
            (collection, ct) => collection.Find(Builders<TestEntity>.Filter.Empty).Project(projection).ToCursorAsync(ct)))
        {
            results.Add(item.Value);
        }

        results.Should().HaveCount(InitialData.Length);
        results.Should().BeEquivalentTo(InitialData.Select(x => x.Value));
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task AggregatePipelineStreams()
    {
        var sut = await GetCollection();

        var pipeline = PipelineDefinition<TestEntity, BsonDocument>.Create(
            "{ $project: { _id: 1, Value: 1 } }");

        var results = new List<BsonDocument>();
        await foreach (var doc in sut.ExecuteManyAsync(
            (collection, ct) => collection.AggregateAsync(pipeline, cancellationToken: ct)))
        {
            results.Add(doc);
        }

        results.Should().HaveCount(InitialData.Length);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task CancellationMidIterationThrows()
    {
        var sut = await GetCollection();
        using var cts = new CancellationTokenSource();

        Func<Task> act = async () =>
        {
            var count = 0;
            await foreach (var item in sut.ExecuteManyAsync(
                (collection, ct) => collection.FindAsync(Builders<TestEntity>.Filter.Empty, new FindOptions<TestEntity> { BatchSize = 1 }, ct),
                cts.Token))
            {
                count++;
                if (count == 1) cts.Cancel();
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task ExceptionInFactorySurfaces()
    {
        var sut = await GetCollection();

        Func<Task> act = async () =>
        {
            await foreach (var _ in sut.ExecuteManyAsync<TestEntity>(
                (_, _) => throw new InvalidOperationException("boom")))
            {
            }
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task EarlyBreakDoesNotLeaveResourceLeaked()
    {
        var sut = await GetCollection();

        var seen = 0;
        await foreach (var _ in sut.ExecuteManyAsync(
            (collection, ct) => collection.FindAsync(Builders<TestEntity>.Filter.Empty, new FindOptions<TestEntity> { BatchSize = 1 }, ct)))
        {
            seen++;
            if (seen == 2) break;
        }
        seen.Should().Be(2);

        var remaining = await sut.GetAsync(x => true).ToArrayAsync();
        remaining.Should().HaveCount(InitialData.Length);
    }
}

[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class LockableExecuteManyAsyncTest : Tharga.MongoDB.Tests.Lockable.Base.LockableTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task StreamsResults()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "a" });
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "b" });

        var count = 0;
        await foreach (var _ in sut.ExecuteManyAsync(
            (collection, ct) => collection.Find(Builders<LockableTestEntity>.Filter.Empty).ToCursorAsync(ct)))
        {
            count++;
        }

        count.Should().Be(2);
    }
}

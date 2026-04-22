using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Covers the driver-cursor-based implementation of <see cref="RepositoryCollectionBase{TEntity, TKey}.GetAsync(System.Linq.Expressions.Expression{System.Func{TEntity, bool}}, Options{TEntity}, System.Threading.CancellationToken)"/>
/// and <c>GetProjectionAsync</c>: multiple batches, preserved options, no skip-penalty semantics.
/// </summary>
[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class GetAsyncStreamingTest : GenericRepositoryCollectionBaseTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task StreamsAllRowsAcrossMultipleBatches()
    {
        var sut = await GetCollection();
        const int total = 250;
        for (var i = 0; i < total; i++)
        {
            await sut.AddAsync(TestEntityFactory.CreateTestEntity());
        }

        var ids = await sut.GetAsync(x => true).Select(x => x.Id).ToArrayAsync();

        ids.Should().HaveCount(total);
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task HonoursOptionsLimit()
    {
        var sut = await GetCollection();
        for (var i = 0; i < 250; i++)
        {
            await sut.AddAsync(TestEntityFactory.CreateTestEntity());
        }

        var items = await sut.GetAsync(Builders<TestEntity>.Filter.Empty, new Options<TestEntity> { Limit = 42 }).ToArrayAsync();

        items.Should().HaveCount(42);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task HonoursOptionsSkip()
    {
        var sut = await GetCollection();
        const int total = 50;
        for (var i = 0; i < total; i++)
        {
            await sut.AddAsync(TestEntityFactory.CreateTestEntity());
        }

        var sorted = await sut.GetAsync(Builders<TestEntity>.Filter.Empty, new Options<TestEntity>
        {
            Sort = Builders<TestEntity>.Sort.Ascending(x => x.Id)
        }).ToArrayAsync();

        var skipped = await sut.GetAsync(Builders<TestEntity>.Filter.Empty, new Options<TestEntity>
        {
            Skip = 10,
            Sort = Builders<TestEntity>.Sort.Ascending(x => x.Id)
        }).ToArrayAsync();

        skipped.Should().HaveCount(total - 10);
        skipped.Select(x => x.Id).Should().Equal(sorted.Skip(10).Select(x => x.Id));
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task HonoursFilterAndSort()
    {
        var sut = await GetCollection();
        for (var i = 0; i < 50; i++)
        {
            await sut.AddAsync(TestEntityFactory.CreateTestEntity());
        }
        var targetSuffix = Guid.NewGuid().ToString();
        for (var i = 0; i < 5; i++)
        {
            var e = TestEntityFactory.CreateTestEntity();
            await sut.AddAsync(e with { ExtraValue = targetSuffix });
        }

        var items = await sut.GetAsync(
            Builders<TestEntity>.Filter.Eq(x => x.ExtraValue, targetSuffix),
            new Options<TestEntity> { Sort = Builders<TestEntity>.Sort.Ascending(x => x.Id) }).ToArrayAsync();

        items.Should().HaveCount(5);
        items.Select(x => x.Id).Should().BeInAscendingOrder();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task ProjectionStreamsAllRows()
    {
        var sut = await GetCollection();
        const int total = 250;
        for (var i = 0; i < total; i++)
        {
            await sut.AddAsync(TestEntityFactory.CreateTestEntity());
        }

        var projected = await sut.GetProjectionAsync<TestProjectionEntity>(x => true).ToArrayAsync();

        projected.Should().HaveCount(total);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task EarlyBreakReleasesCursorCleanly()
    {
        var sut = await GetCollection();
        const int total = 250;
        for (var i = 0; i < total; i++)
        {
            await sut.AddAsync(TestEntityFactory.CreateTestEntity());
        }

        var seen = 0;
        await foreach (var _ in sut.GetAsync(x => true))
        {
            seen++;
            if (seen == 5) break;
        }
        seen.Should().Be(5);

        (await sut.GetAsync(x => true).ToArrayAsync()).Should().HaveCount(total);
    }
}

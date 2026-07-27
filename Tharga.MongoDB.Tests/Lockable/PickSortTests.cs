using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Lockable;
using Tharga.MongoDB.Tests.Lockable.Base;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable;

/// <summary>
/// Ordering control when acquiring a lock (GitHub #135). Without a sort, an arbitrary matching
/// document is locked; these tests pin that a supplied sort decides which one.
/// </summary>
[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class PickSortTests : LockableTestBase
{
    private static readonly SortDefinition<LockableTestEntity> Ascending = Builders<LockableTestEntity>.Sort.Ascending(x => x.Count);
    private static readonly SortDefinition<LockableTestEntity> Descending = Builders<LockableTestEntity>.Sort.Descending(x => x.Count);

    private async Task<LockableTestRepositoryCollection> SeedAsync(params int[] counts)
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        foreach (var count in counts)
        {
            await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId(), Count = count });
        }

        return sut;
    }

    private static async Task<EntityScope<LockableTestEntity, ObjectId>> PickAsync(PickMode mode, LockableTestRepositoryCollection sut, PickOptions<LockableTestEntity> pickOptions)
    {
        return mode == PickMode.Update
            ? await sut.PickForUpdateAsync(x => true, pickOptions)
            : await sut.PickForDeleteAsync(x => true, pickOptions);
    }

    [Theory]
    [InlineData(PickMode.Update)]
    [InlineData(PickMode.Delete)]
    [Trait("Category", "Database")]
    public async Task AscendingSortPicksLowest(PickMode mode)
    {
        //Arrange
        var sut = await SeedAsync(3, 1, 2);

        //Act
        await using var scope = await PickAsync(mode, sut, new PickOptions<LockableTestEntity> { Sort = Ascending });

        //Assert
        scope.Entity.Count.Should().Be(1);
    }

    [Theory]
    [InlineData(PickMode.Update)]
    [InlineData(PickMode.Delete)]
    [Trait("Category", "Database")]
    public async Task DescendingSortPicksHighest(PickMode mode)
    {
        //Arrange — seeded so insertion order alone would return 1, not the expected 3.
        var sut = await SeedAsync(1, 3, 2);

        //Act
        await using var scope = await PickAsync(mode, sut, new PickOptions<LockableTestEntity> { Sort = Descending });

        //Assert
        scope.Entity.Count.Should().Be(3);
    }

    [Theory]
    [InlineData(PickMode.Update)]
    [InlineData(PickMode.Delete)]
    [Trait("Category", "Database")]
    public async Task FilterOverloadHonoursSort(PickMode mode)
    {
        //Arrange
        var sut = await SeedAsync(3, 1, 2);
        var filter = Builders<LockableTestEntity>.Filter.Empty;
        var pickOptions = new PickOptions<LockableTestEntity> { Sort = Ascending };

        //Act
        await using var scope = mode == PickMode.Update
            ? await sut.PickForUpdateAsync(filter, pickOptions)
            : await sut.PickForDeleteAsync(filter, pickOptions);

        //Assert
        scope.Entity.Count.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task SortAppliesWithinTheFilteredSubsetOnly()
    {
        //Arrange — 4 is seeded before 3, so insertion order alone would return 4.
        var sut = await SeedAsync(1, 2, 4, 3);

        //Act
        await using var scope = await sut.PickForUpdateAsync(x => x.Count > 2, new PickOptions<LockableTestEntity> { Sort = Ascending });

        //Assert
        scope.Entity.Count.Should().Be(3, "the sort orders the matching subset, it does not widen it");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task RepeatedSortedPicksDrainInOrder()
    {
        //Arrange — the work-queue scenario from the issue. PickForDelete so each commit
        //removes the document and the next pick sees a smaller queue.
        var sut = await SeedAsync(3, 1, 4, 2);
        var processed = new List<int>();

        //Act
        for (var i = 0; i < 4; i++)
        {
            await using var scope = await sut.PickForDeleteAsync(x => true, new PickOptions<LockableTestEntity> { Sort = Ascending });
            processed.Add(scope.Entity.Count);
            await scope.CommitAsync();
        }

        //Assert
        processed.Should().Equal(1, 2, 3, 4);
        (await sut.CountAsync(x => true)).Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task SortedPickSkipsLockedDocumentAndTakesNextInOrder()
    {
        //Arrange — with 1 locked the remaining insertion order starts at 3, not the expected 2.
        var sut = await SeedAsync(3, 1, 2);
        var first = await sut.GetOneAsync(x => x.Count == 1);
        await sut.PickForUpdateAsync(first.Id, actor: "holder");

        //Act
        await using var scope = await sut.PickForUpdateAsync(x => true, new PickOptions<LockableTestEntity> { Sort = Ascending });

        //Assert
        scope.Entity.Count.Should().Be(2, "the sort-first document is locked, so the next in order is taken");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task SortedPickReclaimsExpiredLockWhenItIsSortFirst()
    {
        //Arrange — 2 is seeded first, so insertion order alone would return it instead of the expired 1.
        var sut = await SeedAsync(2, 1);
        var first = await sut.GetOneAsync(x => x.Count == 1);
        await sut.PickForUpdateAsync(first.Id, actor: "holder", timeout: TimeSpan.Zero);

        //Act
        await using var scope = await sut.PickForUpdateAsync(x => true, new PickOptions<LockableTestEntity> { Sort = Ascending });

        //Assert
        scope.Entity.Count.Should().Be(1, "an expired lock is reclaimable, so it stays the sort-first candidate");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockAsyncHonoursSort()
    {
        //Arrange
        var sut = await SeedAsync(3, 1, 2);

        //Act
        await using var scope = await sut.LockAsync(x => true, new PickOptions<LockableTestEntity> { Sort = Ascending });

        //Assert
        scope.Entity.Count.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockAsyncFilterOverloadHonoursSort()
    {
        //Arrange — seeded so insertion order alone would return 1, not the expected 3.
        var sut = await SeedAsync(1, 3, 2);

        //Act
        await using var scope = await sut.LockAsync(Builders<LockableTestEntity>.Filter.Empty, new PickOptions<LockableTestEntity> { Sort = Descending });

        //Assert
        scope.Entity.Count.Should().Be(3);
    }

    [Theory]
    [InlineData(PickMode.Update)]
    [InlineData(PickMode.Delete)]
    [Trait("Category", "Database")]
    public async Task NullPickOptionsPicksAMatchingDocument(PickMode mode)
    {
        //Arrange
        var sut = await SeedAsync(3, 1, 2);

        //Act
        await using var scope = await PickAsync(mode, sut, null);

        //Assert
        scope.Should().NotBeNull();
        scope.Entity.Count.Should().BeOneOf(1, 2, 3);
    }

    [Theory]
    [InlineData(PickMode.Update)]
    [InlineData(PickMode.Delete)]
    [Trait("Category", "Database")]
    public async Task NullSortPicksAMatchingDocument(PickMode mode)
    {
        //Arrange
        var sut = await SeedAsync(3, 1, 2);

        //Act
        await using var scope = await PickAsync(mode, sut, new PickOptions<LockableTestEntity>());

        //Assert
        scope.Should().NotBeNull();
        scope.Entity.Count.Should().BeOneOf(1, 2, 3);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task SortedPickReturnsNullWhenNothingMatches()
    {
        //Arrange
        var sut = await SeedAsync(1, 2);

        //Act
        await using var scope = await sut.PickForUpdateAsync(x => x.Count > 100, new PickOptions<LockableTestEntity> { Sort = Ascending });

        //Assert
        scope.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task SortedPickCommitPersists()
    {
        //Arrange
        var sut = await SeedAsync(3, 1, 2);
        await using var scope = await sut.PickForUpdateAsync(x => true, new PickOptions<LockableTestEntity> { Sort = Ascending });

        //Act
        await scope.CommitAsync(scope.Entity with { Data = "processed" });

        //Assert
        var post = await sut.GetOneAsync(x => x.Count == 1);
        post.Data.Should().Be("processed");
        post.Lock.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task ConcurrentSortedPicksTakeDistinctDocuments()
    {
        //Arrange — backs the documented claim that racing workers never collide:
        //the loser's filter re-evaluates against the now-locked document.
        var sut = await SeedAsync(3, 1, 2);
        var pickOptions = new PickOptions<LockableTestEntity> { Sort = Ascending };

        //Act
        var scopes = await Task.WhenAll(
            sut.PickForUpdateAsync(x => true, pickOptions),
            sut.PickForUpdateAsync(x => true, pickOptions),
            sut.PickForUpdateAsync(x => true, pickOptions));

        //Assert
        try
        {
            scopes.Should().OnlyContain(x => x != null);
            scopes.Select(x => x.Entity.Count).Should().BeEquivalentTo(new[] { 1, 2, 3 });
        }
        finally
        {
            foreach (var scope in scopes)
            {
                if (scope != null) await scope.DisposeAsync();
            }
        }
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task SortedPickReturnsNullWhenEveryMatchIsLocked()
    {
        //Arrange
        var sut = await SeedAsync(1, 2);
        var pickOptions = new PickOptions<LockableTestEntity> { Sort = Ascending };
        await sut.PickForUpdateAsync(x => true, pickOptions, actor: "holder");
        await sut.PickForUpdateAsync(x => true, pickOptions, actor: "holder");

        //Act
        await using var scope = await sut.PickForUpdateAsync(x => true, pickOptions);

        //Assert
        scope.Should().BeNull("the queue is drained, not blocked");
    }

    public enum PickMode
    {
        Update,
        Delete
    }
}

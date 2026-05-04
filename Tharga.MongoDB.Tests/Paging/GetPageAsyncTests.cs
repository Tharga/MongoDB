using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Paging;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Paging;

[Collection("Sequential")]
public class GetPageAsyncTests : MongoDbTestBase
{
    private readonly PagingTestRepositoryCollection _sut;

    public GetPageAsyncTests()
    {
        _sut = new PagingTestRepositoryCollection(MongoDbServiceFactory, DatabaseContext);
    }

    private async Task SeedAsync(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await _sut.AddAsync(new PagingTestEntity
            {
                Id = ObjectId.GenerateNewId(),
                Name = $"doc-{i:D3}",
                Bucket = (i - 1) / 10, // 0..0..0 (10 each), 1..1..1, ..., 4..4..4
            });
        }
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task First_ReturnsFirstPageAscending()
    {
        await SeedAsync(50);

        var page = await _sut.GetPageAsync(10, PagePosition.First, sortBy: e => e.Name);

        page.Items.Should().HaveCount(10);
        page.Items.Select(x => x.Name).Should().BeInAscendingOrder();
        page.Items[0].Name.Should().Be("doc-001");
        page.Items[9].Name.Should().Be("doc-010");
        page.HasNext.Should().BeTrue();
        page.HasPrevious.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task After_FromLastCursor_ReturnsNextPage()
    {
        await SeedAsync(50);
        var first = await _sut.GetPageAsync(10, PagePosition.First, sortBy: e => e.Name);

        var second = await _sut.GetPageAsync(10, PagePosition.After(first.LastCursor), sortBy: e => e.Name);

        second.Items.Should().HaveCount(10);
        second.Items[0].Name.Should().Be("doc-011");
        second.Items[9].Name.Should().Be("doc-020");
        second.HasNext.Should().BeTrue();
        second.HasPrevious.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task Before_FromFirstCursor_ReturnsPreviousPage()
    {
        await SeedAsync(50);
        var first = await _sut.GetPageAsync(10, PagePosition.First, sortBy: e => e.Name);
        var second = await _sut.GetPageAsync(10, PagePosition.After(first.LastCursor), sortBy: e => e.Name);

        var back = await _sut.GetPageAsync(10, PagePosition.Before(second.FirstCursor), sortBy: e => e.Name);

        back.Items.Should().HaveCount(10);
        back.Items.Select(x => x.Name).Should().BeInAscendingOrder();
        back.Items[0].Name.Should().Be("doc-001");
        back.Items[9].Name.Should().Be("doc-010");
        back.HasNext.Should().BeTrue();
        back.HasPrevious.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task Last_ReturnsFinalPageInAscendingOrder()
    {
        await SeedAsync(50);

        var page = await _sut.GetPageAsync(10, PagePosition.Last, sortBy: e => e.Name);

        page.Items.Should().HaveCount(10);
        page.Items.Select(x => x.Name).Should().BeInAscendingOrder();
        page.Items[0].Name.Should().Be("doc-041");
        page.Items[9].Name.Should().Be("doc-050");
        page.HasNext.Should().BeFalse();
        page.HasPrevious.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task Last_PartialFinalPage()
    {
        await SeedAsync(47); // last page has 7 items (8th page, partial)

        var page = await _sut.GetPageAsync(10, PagePosition.Last, sortBy: e => e.Name);

        // The "Last" page is the trailing pageSize items in ascending order — last 10 items of the 47.
        page.Items.Should().HaveCount(10);
        page.Items[0].Name.Should().Be("doc-038");
        page.Items[9].Name.Should().Be("doc-047");
        page.HasNext.Should().BeFalse();
        page.HasPrevious.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task PageStep_JumpsForward()
    {
        await SeedAsync(50);
        var first = await _sut.GetPageAsync(10, PagePosition.First, sortBy: e => e.Name);

        // Skip page 2, land on page 3
        var third = await _sut.GetPageAsync(10, PagePosition.After(first.LastCursor, pageStep: 1), sortBy: e => e.Name);

        third.Items[0].Name.Should().Be("doc-021");
        third.Items[9].Name.Should().Be("doc-030");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task IdAscending_NoExplicitSort_UsesIdPath()
    {
        await SeedAsync(15);

        var page = await _sut.GetPageAsync(10, PagePosition.First); // sortBy null → _id ascending

        page.Items.Should().HaveCount(10);
        page.Items.Select(x => x.Id).Should().BeInAscendingOrder();
        page.HasNext.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task IdDescending_NoExplicitSort()
    {
        await SeedAsync(15);

        var page = await _sut.GetPageAsync(10, PagePosition.First, ascending: false);

        page.Items.Should().HaveCount(10);
        page.Items.Select(x => x.Id).Should().BeInDescendingOrder();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task NonUniqueSort_TiebreakerByIdPreservesOrder()
    {
        // 50 docs across 5 Bucket values (10 docs per bucket). Bucket alone is non-unique,
        // so the cursor must include the _id tiebreaker to avoid skipping or duplicating.
        await SeedAsync(50);

        var first = await _sut.GetPageAsync(10, PagePosition.First, sortBy: e => e.Bucket);
        var second = await _sut.GetPageAsync(10, PagePosition.After(first.LastCursor), sortBy: e => e.Bucket);
        var third = await _sut.GetPageAsync(10, PagePosition.After(second.LastCursor), sortBy: e => e.Bucket);

        var allFetched = first.Items.Concat(second.Items).Concat(third.Items).ToArray();
        allFetched.Should().HaveCount(30);
        allFetched.Select(x => x.Id).Should().OnlyHaveUniqueItems();
        allFetched.Select(x => x.Bucket).Should().BeInAscendingOrder();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task PredicateAndCursor_ComposeCorrectly()
    {
        await SeedAsync(50);

        // Bucket > 2 → buckets 3 + 4 → 20 docs total (doc-031..doc-050)
        var first = await _sut.GetPageAsync(10, PagePosition.First, predicate: e => e.Bucket > 2, sortBy: e => e.Name);
        var second = await _sut.GetPageAsync(10, PagePosition.After(first.LastCursor), predicate: e => e.Bucket > 2, sortBy: e => e.Name);

        first.Items.Select(x => x.Name).Should().BeEquivalentTo(Enumerable.Range(31, 10).Select(i => $"doc-{i:D3}"));
        second.Items.Select(x => x.Name).Should().BeEquivalentTo(Enumerable.Range(41, 10).Select(i => $"doc-{i:D3}"));
        second.HasNext.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task EmptyCollection_AllPositions_ReturnEmptyPage()
    {
        var first = await _sut.GetPageAsync(10, PagePosition.First, sortBy: e => e.Name);
        var last = await _sut.GetPageAsync(10, PagePosition.Last, sortBy: e => e.Name);

        first.Items.Should().BeEmpty();
        first.HasNext.Should().BeFalse();
        first.HasPrevious.Should().BeFalse();
        first.FirstCursor.IsEmpty.Should().BeTrue();
        first.LastCursor.IsEmpty.Should().BeTrue();

        last.Items.Should().BeEmpty();
        last.HasNext.Should().BeFalse();
        last.HasPrevious.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task Projection_ReturnsProjectedShape()
    {
        await SeedAsync(15);

        var page = await _sut.GetPageProjectionAsync(10, PagePosition.First, projection: e => e.Name, sortBy: e => e.Name);

        page.Items.Should().HaveCount(10);
        page.Items[0].Should().Be("doc-001");
        page.Items[9].Should().Be("doc-010");
        page.HasNext.Should().BeTrue();
        page.HasPrevious.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task CursorFromDifferentSort_Throws()
    {
        await SeedAsync(20);
        var page = await _sut.GetPageAsync(10, PagePosition.First, sortBy: e => e.Name);

        Func<Task> act = () => _sut.GetPageAsync(10, PagePosition.After(page.LastCursor), sortBy: e => e.Bucket);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not transferable across sorts*");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task PageSizeZero_Throws()
    {
        Func<Task> act = () => _sut.GetPageAsync(0, PagePosition.First, sortBy: e => e.Name);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DescendingSort_FirstReturnsHighestNamesDescending()
    {
        await SeedAsync(50);

        var page = await _sut.GetPageAsync(10, PagePosition.First, sortBy: e => e.Name, ascending: false);

        page.Items.Select(x => x.Name).Should().BeInDescendingOrder();
        page.Items[0].Name.Should().Be("doc-050");
        page.Items[9].Name.Should().Be("doc-041");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DescendingSort_AfterCursorContinuesDescending()
    {
        await SeedAsync(50);
        var first = await _sut.GetPageAsync(10, PagePosition.First, sortBy: e => e.Name, ascending: false);

        var second = await _sut.GetPageAsync(10, PagePosition.After(first.LastCursor), sortBy: e => e.Name, ascending: false);

        second.Items.Select(x => x.Name).Should().BeInDescendingOrder();
        second.Items[0].Name.Should().Be("doc-040");
        second.Items[9].Name.Should().Be("doc-031");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockableCollection_DelegatesToDisk()
    {
        var lockableSut = new PagingLockableTestRepositoryCollection(MongoDbServiceFactory);
        for (var i = 1; i <= 25; i++)
        {
            await lockableSut.AddAsync(new PagingLockableTestEntity
            {
                Id = ObjectId.GenerateNewId(),
                Name = $"lock-{i:D3}",
                Bucket = (i - 1) / 5,
            });
        }

        var first = await lockableSut.GetPageAsync(10, PagePosition.First, sortBy: e => e.Name);
        var second = await lockableSut.GetPageAsync(10, PagePosition.After(first.LastCursor), sortBy: e => e.Name);

        first.Items.Select(x => x.Name).Should().BeEquivalentTo(Enumerable.Range(1, 10).Select(i => $"lock-{i:D3}"));
        second.Items.Select(x => x.Name).Should().BeEquivalentTo(Enumerable.Range(11, 10).Select(i => $"lock-{i:D3}"));
        second.HasNext.Should().BeTrue();
        second.HasPrevious.Should().BeTrue();
    }
}

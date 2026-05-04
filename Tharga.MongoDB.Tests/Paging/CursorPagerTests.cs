using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Paging;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Paging;

[Collection("Sequential")]
public class CursorPagerTests : MongoDbTestBase
{
    private readonly PagingTestRepositoryCollection _sut;

    public CursorPagerTests()
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
                Bucket = (i - 1) / 10,
            });
        }
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task SequentialNext_ReturnsCorrectPagesAndStableTotal()
    {
        await SeedAsync(50);
        var pager = new CursorPager<PagingTestEntity, ObjectId>(_sut);

        var (page1, total1) = await pager.LoadAsync(0, 10, sortBy: e => e.Name);
        var (page2, total2) = await pager.LoadAsync(10, 10, sortBy: e => e.Name);
        var (page3, total3) = await pager.LoadAsync(20, 10, sortBy: e => e.Name);

        page1.Select(x => x.Name).Should().BeEquivalentTo(Enumerable.Range(1, 10).Select(i => $"doc-{i:D3}"));
        page2.Select(x => x.Name).Should().BeEquivalentTo(Enumerable.Range(11, 10).Select(i => $"doc-{i:D3}"));
        page3.Select(x => x.Name).Should().BeEquivalentTo(Enumerable.Range(21, 10).Select(i => $"doc-{i:D3}"));
        total1.Should().Be(50);
        total2.Should().Be(50);
        total3.Should().Be(50);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LastPage_FromTrailingSkip_ReturnsFinalItems()
    {
        await SeedAsync(50);
        var pager = new CursorPager<PagingTestEntity, ObjectId>(_sut);

        var (items, total) = await pager.LoadAsync(40, 10, sortBy: e => e.Name);

        total.Should().Be(50);
        items.Select(x => x.Name).Should().BeEquivalentTo(Enumerable.Range(41, 10).Select(i => $"doc-{i:D3}"));
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task Previous_FromKeysetState_ReturnsEarlierPage()
    {
        await SeedAsync(50);
        var pager = new CursorPager<PagingTestEntity, ObjectId>(_sut);

        await pager.LoadAsync(0, 10, sortBy: e => e.Name);
        await pager.LoadAsync(10, 10, sortBy: e => e.Name);
        var (back, _) = await pager.LoadAsync(0, 10, sortBy: e => e.Name);

        back.Select(x => x.Name).Should().BeEquivalentTo(Enumerable.Range(1, 10).Select(i => $"doc-{i:D3}"));
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task TwoPageJump_StaysOnKeysetPath()
    {
        await SeedAsync(50);
        var pager = new CursorPager<PagingTestEntity, ObjectId>(_sut);

        await pager.LoadAsync(0, 10, sortBy: e => e.Name);
        var (jumped, _) = await pager.LoadAsync(20, 10, sortBy: e => e.Name);

        jumped.Select(x => x.Name).Should().BeEquivalentTo(Enumerable.Range(21, 10).Select(i => $"doc-{i:D3}"));
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task ArbitraryJump_FallbackThenResumeKeyset()
    {
        // 100 docs, page size 10. Jump from page 1 → page 8 (skip=70, jump of 7 pages > MaxJumpPages=5).
        // That should fall back to GetManyAsync skip=70. The next LoadAsync(80, 10) should resume keyset
        // navigation from the cursor re-issued at the end of the fallback fetch.
        await SeedAsync(100);
        var pager = new CursorPager<PagingTestEntity, ObjectId>(_sut);

        await pager.LoadAsync(0, 10, sortBy: e => e.Name);
        var (jumped, _) = await pager.LoadAsync(70, 10, sortBy: e => e.Name);
        var (next, _) = await pager.LoadAsync(80, 10, sortBy: e => e.Name);

        jumped.Select(x => x.Name).Should().BeEquivalentTo(Enumerable.Range(71, 10).Select(i => $"doc-{i:D3}"));
        next.Select(x => x.Name).Should().BeEquivalentTo(Enumerable.Range(81, 10).Select(i => $"doc-{i:D3}"));
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task FilterChange_RecountsAndResetsCursors()
    {
        await SeedAsync(50);
        var pager = new CursorPager<PagingTestEntity, ObjectId>(_sut);

        var (allPage, totalAll) = await pager.LoadAsync(0, 10, sortBy: e => e.Name);
        // change filter → cache should invalidate and re-count
        var (filtered, totalFiltered) = await pager.LoadAsync(0, 10, predicate: e => e.Bucket > 2, sortBy: e => e.Name);
        // load same filter again — count cached
        var (filteredAgain, totalFiltered2) = await pager.LoadAsync(0, 10, predicate: e => e.Bucket > 2, sortBy: e => e.Name);

        totalAll.Should().Be(50);
        totalFiltered.Should().Be(20);
        totalFiltered2.Should().Be(20);
        allPage.Select(x => x.Name).Should().BeEquivalentTo(Enumerable.Range(1, 10).Select(i => $"doc-{i:D3}"));
        filtered.Select(x => x.Name).Should().BeEquivalentTo(Enumerable.Range(31, 10).Select(i => $"doc-{i:D3}"));
        filteredAgain.Select(x => x.Name).Should().BeEquivalentTo(Enumerable.Range(31, 10).Select(i => $"doc-{i:D3}"));
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task Reset_ClearsStateAndRecounts()
    {
        await SeedAsync(50);
        var pager = new CursorPager<PagingTestEntity, ObjectId>(_sut);

        await pager.LoadAsync(0, 10, sortBy: e => e.Name);
        await pager.LoadAsync(10, 10, sortBy: e => e.Name);

        pager.Reset();

        var (firstAgain, total) = await pager.LoadAsync(0, 10, sortBy: e => e.Name);
        firstAgain.Select(x => x.Name).Should().BeEquivalentTo(Enumerable.Range(1, 10).Select(i => $"doc-{i:D3}"));
        total.Should().Be(50);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task EmptyCollection_ReturnsEmptyAndZeroCount()
    {
        var pager = new CursorPager<PagingTestEntity, ObjectId>(_sut);

        var (items, total) = await pager.LoadAsync(0, 10, sortBy: e => e.Name);

        items.Should().BeEmpty();
        total.Should().Be(0);
    }
}

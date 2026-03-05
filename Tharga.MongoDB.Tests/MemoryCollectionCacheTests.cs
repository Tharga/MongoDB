using System.Threading.Tasks;
using FluentAssertions;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class MemoryCollectionCacheTests
{
    [Fact]
    public async Task LoadAllAsync_ReturnsEmptyList()
    {
        var cache = new MemoryCollectionCache();

        var result = await cache.LoadAllAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_IsNoOp()
    {
        var cache = new MemoryCollectionCache();

        await cache.SaveAsync(null);
    }

    [Fact]
    public async Task DeleteAsync_IsNoOp()
    {
        var cache = new MemoryCollectionCache();

        await cache.DeleteAsync("db", "col");
    }

    [Fact]
    public async Task ResetAsync_IsNoOp()
    {
        var cache = new MemoryCollectionCache();

        await cache.ResetAsync();
    }
}

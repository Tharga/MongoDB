using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class MemoryCollectionCacheTests
{
    private static CollectionInfo CreateInfo(string key = "cfg.db.col")
    {
        var parts = key.Split('.');
        return new CollectionInfo
        {
            ConfigurationName = parts[0],
            DatabaseName = parts[1],
            CollectionName = parts[2],
            Server = "localhost",
            Registration = Registration.Static,
            EntityTypes = new[] { "TestEntity" },
            CollectionType = typeof(MemoryCollectionCacheTests),
        };
    }

    [Fact]
    public async Task LoadAsync_IsNoOp()
    {
        var cache = new MemoryCollectionCache();

        await cache.LoadAsync();

        cache.GetAll().Should().BeEmpty();
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
    public async Task ResetAsync_ClearsDictionary()
    {
        var cache = new MemoryCollectionCache();
        var info = CreateInfo();
        cache.Set(info.Key, info);
        cache.GetAll().Should().HaveCount(1);

        await cache.ResetAsync();

        cache.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void TryGet_ReturnsFalse_WhenKeyNotPresent()
    {
        var cache = new MemoryCollectionCache();

        var found = cache.TryGet("nonexistent", out var value);

        found.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void Set_TryGet_RoundTrip()
    {
        var cache = new MemoryCollectionCache();
        var info = CreateInfo();

        cache.Set(info.Key, info);
        var found = cache.TryGet(info.Key, out var value);

        found.Should().BeTrue();
        value.Should().BeSameAs(info);
    }

    [Fact]
    public void AddOrUpdate_AddsNewEntry()
    {
        var cache = new MemoryCollectionCache();
        var info = CreateInfo();

        var result = cache.AddOrUpdate(info.Key,
            _ => info,
            (_, existing) => existing);

        result.Should().BeSameAs(info);
        cache.TryGet(info.Key, out var stored).Should().BeTrue();
        stored.Should().BeSameAs(info);
    }

    [Fact]
    public void AddOrUpdate_UpdatesExistingEntry()
    {
        var cache = new MemoryCollectionCache();
        var info = CreateInfo();
        cache.Set(info.Key, info);
        var updated = info with { Server = "updated" };

        var result = cache.AddOrUpdate(info.Key,
            _ => info,
            (_, _) => updated);

        result.Server.Should().Be("updated");
    }

    [Fact]
    public void TryRemove_RemovesEntry()
    {
        var cache = new MemoryCollectionCache();
        var info = CreateInfo();
        cache.Set(info.Key, info);

        var removed = cache.TryRemove(info.Key, out var value);

        removed.Should().BeTrue();
        value.Should().BeSameAs(info);
        cache.TryGet(info.Key, out _).Should().BeFalse();
    }

    [Fact]
    public void TryRemove_ReturnsFalse_WhenKeyNotPresent()
    {
        var cache = new MemoryCollectionCache();

        var removed = cache.TryRemove("nonexistent", out var value);

        removed.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void GetAll_ReturnsAllEntries()
    {
        var cache = new MemoryCollectionCache();
        var info1 = CreateInfo("cfg.db.col1");
        var info2 = CreateInfo("cfg.db.col2");
        cache.Set(info1.Key, info1);
        cache.Set(info2.Key, info2);

        var all = cache.GetAll().ToArray();

        all.Should().HaveCount(2);
    }

    [Fact]
    public void GetKeys_ReturnsAllKeys()
    {
        var cache = new MemoryCollectionCache();
        var info1 = CreateInfo("cfg.db.col1");
        var info2 = CreateInfo("cfg.db.col2");
        cache.Set(info1.Key, info1);
        cache.Set(info2.Key, info2);

        var keys = cache.GetKeys().ToArray();

        keys.Should().HaveCount(2);
        keys.Should().Contain(info1.Key);
        keys.Should().Contain(info2.Key);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var cache = new MemoryCollectionCache();
        var info = CreateInfo();
        cache.Set(info.Key, info);

        cache.Clear();

        cache.GetAll().Should().BeEmpty();
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class MemoryCollectionCacheTests
{
    private static CollectionInfo CreateInfo(string configName = "cfg", string dbName = "db", string colName = "col")
    {
        return new CollectionInfo
        {
            ConfigurationName = configName,
            DatabaseName = dbName,
            CollectionName = colName,
            Server = "localhost",
            Registration = Registration.Static,
            Types = Array.Empty<string>(),
            CollectionType = typeof(object),
        };
    }

    [Fact]
    public void TryGet_Empty_ReturnsFalse()
    {
        var cache = new MemoryCollectionCache();

        var result = cache.TryGet("key", out var value);

        result.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void AddOrUpdate_NewKey_AddsEntry()
    {
        var cache = new MemoryCollectionCache();
        var info = CreateInfo();

        var result = cache.AddOrUpdate("key1", _ => info, (_, existing) => existing);

        result.Should().BeSameAs(info);
        cache.TryGet("key1", out var stored).Should().BeTrue();
        stored.Should().BeSameAs(info);
    }

    [Fact]
    public void AddOrUpdate_ExistingKey_UpdatesEntry()
    {
        var cache = new MemoryCollectionCache();
        var original = CreateInfo(colName: "original");
        var updated = CreateInfo(colName: "updated");
        cache.AddOrUpdate("key1", _ => original, (_, existing) => existing);

        cache.AddOrUpdate("key1", _ => throw new Exception("Should not add"), (_, _) => updated);

        cache.TryGet("key1", out var stored).Should().BeTrue();
        stored.CollectionName.Should().Be("updated");
    }

    [Fact]
    public void GetAll_ReturnsAllEntries()
    {
        var cache = new MemoryCollectionCache();
        var info1 = CreateInfo(colName: "col1");
        var info2 = CreateInfo(colName: "col2");
        cache.AddOrUpdate("key1", _ => info1, (_, e) => e);
        cache.AddOrUpdate("key2", _ => info2, (_, e) => e);

        var all = cache.GetAll().ToList();

        all.Should().HaveCount(2);
        all.Should().Contain(x => x.CollectionName == "col1");
        all.Should().Contain(x => x.CollectionName == "col2");
    }

    [Fact]
    public void Remove_ExistingKey_ReturnsTrueAndValue()
    {
        var cache = new MemoryCollectionCache();
        var info = CreateInfo();
        cache.AddOrUpdate("key1", _ => info, (_, e) => e);

        var result = cache.Remove("key1", out var removed);

        result.Should().BeTrue();
        removed.Should().BeSameAs(info);
        cache.TryGet("key1", out _).Should().BeFalse();
    }

    [Fact]
    public void Remove_NonExistentKey_ReturnsFalse()
    {
        var cache = new MemoryCollectionCache();

        var result = cache.Remove("missing", out var removed);

        result.Should().BeFalse();
        removed.Should().BeNull();
    }

    [Fact]
    public async Task ResetAsync_ClearsAllEntries()
    {
        var cache = new MemoryCollectionCache();
        cache.AddOrUpdate("key1", _ => CreateInfo(), (_, e) => e);
        cache.AddOrUpdate("key2", _ => CreateInfo(), (_, e) => e);

        await cache.ResetAsync();

        cache.GetAll().Should().BeEmpty();
        cache.TryGet("key1", out _).Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_CompletesImmediately()
    {
        var cache = new MemoryCollectionCache();

        await cache.InitializeAsync();

        // No-op; just verifies it doesn't throw
    }

    [Fact]
    public async Task SaveAsync_IsNoOp()
    {
        var cache = new MemoryCollectionCache();
        var info = CreateInfo();

        await cache.SaveAsync(info);

        // No persistence; just verifies it doesn't throw
    }

    [Fact]
    public async Task DeleteAsync_IsNoOp()
    {
        var cache = new MemoryCollectionCache();

        await cache.DeleteAsync("db", "col");

        // No persistence; just verifies it doesn't throw
    }
}

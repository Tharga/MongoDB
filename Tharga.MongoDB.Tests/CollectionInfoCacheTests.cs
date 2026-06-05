using FluentAssertions;
using System.Linq;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class CollectionInfoCacheTests
{
    private static CollectionInfo NewInfo(string config, string database, string collection)
    {
        return new CollectionInfo
        {
            ConfigurationName = (ConfigurationName)config,
            DatabaseName = database,
            CollectionName = collection,
            Registration = Registration.Static,
            Server = "localhost",
            CollectionType = typeof(object),
            EntityTypes = new[] { "TestEntity" },
        };
    }

    [Fact]
    public void IsEmpty_True_OnFreshCache()
    {
        var sut = new CollectionInfoCache();

        sut.IsEmpty.Should().BeTrue();
        sut.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Upsert_AddsThenOverwrites()
    {
        var sut = new CollectionInfoCache();
        var first = NewInfo("A", "Db", "Coll");
        var second = NewInfo("A", "Db", "Coll");

        sut.Upsert(first);
        sut.Upsert(second);

        sut.GetAll().Should().HaveCount(1);
        sut.TryGet(first.Key, out var entry).Should().BeTrue();
        entry.Info.Should().BeSameAs(second);
    }

    [Fact]
    public void Remove_DropsEntry()
    {
        var sut = new CollectionInfoCache();
        var info = NewInfo("A", "Db", "Coll");
        sut.Upsert(info);

        sut.Remove(info.Key);

        sut.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void GetAll_ReturnsAllSeparateCollections()
    {
        var sut = new CollectionInfoCache();
        sut.Upsert(NewInfo("A", "Db", "One"));
        sut.Upsert(NewInfo("A", "Db", "Two"));
        sut.Upsert(NewInfo("B", "Other", "Three"));

        sut.GetAll().Should().HaveCount(3);
    }

    [Fact]
    public async Task Upsert_IsThreadSafe()
    {
        var sut = new CollectionInfoCache();

        // 32 parallel writers, each upserting 100 distinct collections.
        // No exception + final state count must match.
        await Task.WhenAll(Enumerable.Range(0, 32).Select(thread => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                sut.Upsert(NewInfo("A", "Db", $"Coll_{thread}_{i}"));
            }
        })));

        sut.GetAll().Should().HaveCount(32 * 100);
    }
}

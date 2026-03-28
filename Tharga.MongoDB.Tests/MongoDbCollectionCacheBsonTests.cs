using System;
using System.Linq;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class MongoDbCollectionCacheBsonTests
{
    private static CollectionInfo CreateFullInfo()
    {
        return new CollectionInfo
        {
            ConfigurationName = "testConfig",
            DatabaseName = "testDb",
            CollectionName = "testCol",
            Server = "localhost:27017",
            DatabasePart = "part1",
            Discovery = Discovery.Database | Discovery.Registration,
            Registration = Registration.Static,
            EntityTypes = new[] { "TypeA", "TypeB" },
            CollectionType = typeof(MongoDbCollectionCacheBsonTests),
            Stats = new CollectionStats
            {
                DocumentCount = 100,
                Size = 2048,
                UpdatedAt = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc),
            },
            Index = new IndexInfo
            {
                Current = new[]
                {
                    new IndexMeta { Name = "_id_", Fields = new[] { "_id" }, IsUnique = true },
                    new IndexMeta { Name = "Name_1", Fields = new[] { "Name" }, IsUnique = false },
                },
                Defined = Array.Empty<IndexMeta>(),
                UpdatedAt = new DateTime(2025, 6, 15, 11, 0, 0, DateTimeKind.Utc),
            },
        };
    }

    [Fact]
    public void MonitorKey_CombinesDatabaseAndCollection()
    {
        var key = MongoDbCollectionCache.MonitorKey("myDb", "myCol");

        key.Should().Be("myDb/myCol");
    }

    [Fact]
    public void ToBson_NonNull_ReturnsBsonString()
    {
        var result = MongoDbCollectionCache.ToBson("hello");

        result.Should().BeOfType<BsonString>();
        result.AsString.Should().Be("hello");
    }

    [Fact]
    public void ToBson_Null_ReturnsBsonNull()
    {
        var result = MongoDbCollectionCache.ToBson(null);

        result.Should().Be(BsonNull.Value);
    }

    [Fact]
    public void BsonStr_NonNull_ReturnsString()
    {
        var result = MongoDbCollectionCache.BsonStr(new BsonString("test"));

        result.Should().Be("test");
    }

    [Fact]
    public void BsonStr_BsonNull_ReturnsNull()
    {
        var result = MongoDbCollectionCache.BsonStr(BsonNull.Value);

        result.Should().BeNull();
    }

    [Fact]
    public void BsonStr_Null_ReturnsNull()
    {
        var result = MongoDbCollectionCache.BsonStr(null);

        result.Should().BeNull();
    }

    [Fact]
    public void IndexesToBson_Null_ReturnsEmptyArray()
    {
        var result = MongoDbCollectionCache.IndexesToBson(null);

        result.Should().BeOfType<BsonArray>();
        result.Count.Should().Be(0);
    }

    [Fact]
    public void IndexesToBson_WithIndexes_SerializesCorrectly()
    {
        var indexes = new[]
        {
            new IndexMeta { Name = "idx1", Fields = new[] { "A", "B" }, IsUnique = true },
        };

        var result = MongoDbCollectionCache.IndexesToBson(indexes);

        result.Count.Should().Be(1);
        var doc = result[0].AsBsonDocument;
        doc["Name"].AsString.Should().Be("idx1");
        doc["Fields"].AsBsonArray.Select(f => f.AsString).Should().BeEquivalentTo("A", "B");
        doc["IsUnique"].AsBoolean.Should().BeTrue();
    }

    [Fact]
    public void BsonToIndexes_Null_ReturnsNull()
    {
        var result = MongoDbCollectionCache.BsonToIndexes(null);

        result.Should().BeNull();
    }

    [Fact]
    public void BsonToIndexes_BsonNull_ReturnsNull()
    {
        var result = MongoDbCollectionCache.BsonToIndexes(BsonNull.Value);

        result.Should().BeNull();
    }

    [Fact]
    public void IndexesToBson_BsonToIndexes_RoundTrip()
    {
        var indexes = new[]
        {
            new IndexMeta { Name = "_id_", Fields = new[] { "_id" }, IsUnique = true },
            new IndexMeta { Name = "Name_1_Age_-1", Fields = new[] { "Name", "Age" }, IsUnique = false },
        };

        var bson = MongoDbCollectionCache.IndexesToBson(indexes);
        var result = MongoDbCollectionCache.BsonToIndexes(bson);

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("_id_");
        result[0].Fields.Should().BeEquivalentTo("_id");
        result[0].IsUnique.Should().BeTrue();
        result[1].Name.Should().Be("Name_1_Age_-1");
        result[1].Fields.Should().BeEquivalentTo("Name", "Age");
        result[1].IsUnique.Should().BeFalse();
    }

    [Fact]
    public void CollectionInfoToBson_SerializesAllFields()
    {
        var info = CreateFullInfo();
        var id = MongoDbCollectionCache.MonitorKey(info.DatabaseName, info.CollectionName);

        var doc = MongoDbCollectionCache.CollectionInfoToBson(id, info);

        doc["_id"].AsString.Should().Be("testDb/testCol");
        doc["CollectionName"].AsString.Should().Be("testCol");
        doc["ConfigurationName"].AsString.Should().Be("testConfig");
        doc["DatabaseName"].AsString.Should().Be("testDb");
        doc["Server"].AsString.Should().Be("localhost:27017");
        doc["DatabasePart"].AsString.Should().Be("part1");
        doc["Source"].AsInt32.Should().Be((int)(Discovery.Database | Discovery.Registration));
        doc["Registration"].AsInt32.Should().Be((int)Registration.Static);
        doc["Types"].AsBsonArray.Select(t => t.AsString).Should().BeEquivalentTo("TypeA", "TypeB");
        doc["DocumentCount"].AsInt64.Should().Be(100);
        doc["Size"].AsInt64.Should().Be(2048);
        doc["CurrentIndexes"].AsBsonArray.Count.Should().Be(2);
        doc["StatsUpdatedAt"].ToUniversalTime().Should().Be(new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc));
        doc["IndexUpdatedAt"].ToUniversalTime().Should().Be(new DateTime(2025, 6, 15, 11, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void CollectionInfoToBson_NullTimestamps_SerializeAsBsonNull()
    {
        var info = CreateFullInfo() with
        {
            Stats = new CollectionStats { DocumentCount = 100, Size = 2048, UpdatedAt = null },
            Index = new IndexInfo { Current = info_index_current(), Defined = Array.Empty<IndexMeta>(), UpdatedAt = null },
        };
        var id = MongoDbCollectionCache.MonitorKey(info.DatabaseName, info.CollectionName);

        var doc = MongoDbCollectionCache.CollectionInfoToBson(id, info);

        doc["StatsUpdatedAt"].IsBsonNull.Should().BeTrue();
        doc["IndexUpdatedAt"].IsBsonNull.Should().BeTrue();

        static IndexMeta[] info_index_current() => new[]
        {
            new IndexMeta { Name = "_id_", Fields = new[] { "_id" }, IsUnique = true },
            new IndexMeta { Name = "Name_1", Fields = new[] { "Name" }, IsUnique = false },
        };
    }

    [Fact]
    public void CollectionInfoToBson_NullStats_SerializesAsZero()
    {
        var info = CreateFullInfo() with { Stats = null };
        var id = MongoDbCollectionCache.MonitorKey(info.DatabaseName, info.CollectionName);

        var doc = MongoDbCollectionCache.CollectionInfoToBson(id, info);

        doc["DocumentCount"].AsInt64.Should().Be(0);
        doc["Size"].AsInt64.Should().Be(0);
        doc["StatsUpdatedAt"].IsBsonNull.Should().BeTrue();
    }

    [Fact]
    public void CollectionInfoToBson_NullIndex_SerializesAsEmptyArray()
    {
        var info = CreateFullInfo() with { Index = null };
        var id = MongoDbCollectionCache.MonitorKey(info.DatabaseName, info.CollectionName);

        var doc = MongoDbCollectionCache.CollectionInfoToBson(id, info);

        doc["CurrentIndexes"].AsBsonArray.Count.Should().Be(0);
        doc["IndexUpdatedAt"].IsBsonNull.Should().BeTrue();
    }

    [Fact]
    public void BsonToCollectionInfo_FullRoundTrip()
    {
        var original = CreateFullInfo();
        var id = MongoDbCollectionCache.MonitorKey(original.DatabaseName, original.CollectionName);
        var doc = MongoDbCollectionCache.CollectionInfoToBson(id, original);

        var result = MongoDbCollectionCache.BsonToCollectionInfo(doc, "testConfig");

        result.Should().NotBeNull();
        result.CollectionName.Should().Be("testCol");
        result.DatabaseName.Should().Be("testDb");
        result.Server.Should().Be("localhost:27017");
        result.DatabasePart.Should().Be("part1");
        result.Discovery.Should().Be(Discovery.Database | Discovery.Registration);
        result.Registration.Should().Be(Registration.Static);
        result.EntityTypes.Should().BeEquivalentTo("TypeA", "TypeB");
        result.CollectionType.Should().Be(typeof(MongoDbCollectionCacheBsonTests));
        result.Stats.Should().NotBeNull();
        result.Stats.DocumentCount.Should().Be(100);
        result.Stats.Size.Should().Be(2048);
        result.Stats.UpdatedAt.Should().Be(new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc));
        result.Index.Current.Should().HaveCount(2);
        result.Index.UpdatedAt.Should().Be(new DateTime(2025, 6, 15, 11, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void BsonToCollectionInfo_NullTimestamps_DeserializeAsNull()
    {
        var info = CreateFullInfo() with
        {
            Stats = new CollectionStats { DocumentCount = 100, Size = 2048, UpdatedAt = null },
            Index = CreateFullInfo().Index with { UpdatedAt = null },
        };
        var id = MongoDbCollectionCache.MonitorKey(info.DatabaseName, info.CollectionName);
        var doc = MongoDbCollectionCache.CollectionInfoToBson(id, info);

        var result = MongoDbCollectionCache.BsonToCollectionInfo(doc, "testConfig");

        result.Stats.UpdatedAt.Should().BeNull();
        result.Index.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void BsonToCollectionInfo_ZeroDocumentCount_DeserializesAsNullStats()
    {
        var info = CreateFullInfo() with { Stats = null };
        var id = MongoDbCollectionCache.MonitorKey(info.DatabaseName, info.CollectionName);
        var doc = MongoDbCollectionCache.CollectionInfoToBson(id, info);

        var result = MongoDbCollectionCache.BsonToCollectionInfo(doc, "testConfig");

        result.Stats.Should().BeNull();
    }

    [Fact]
    public void BsonToCollectionInfo_MissingDatabaseName_ReturnsNull()
    {
        var doc = new BsonDocument
        {
            { "_id", "testCol" },
            { "CollectionName", "testCol" },
        };

        var result = MongoDbCollectionCache.BsonToCollectionInfo(doc, "cfg");

        result.Should().BeNull();
    }

    [Fact]
    public void BsonToCollectionInfo_LegacyIdFormat_ExtractsCollectionName()
    {
        // Legacy format: _id is just the collection name (no slash)
        var doc = new BsonDocument
        {
            { "_id", "legacyCol" },
            { "DatabaseName", "myDb" },
            { "Server", "localhost" },
            { "Registration", 1 },
            { "Types", new BsonArray() },
            { "Source", 1 },
        };

        var result = MongoDbCollectionCache.BsonToCollectionInfo(doc, "cfg");

        result.Should().NotBeNull();
        result.CollectionName.Should().Be("legacyCol");
    }

    [Fact]
    public void BsonToCollectionInfo_CompositeIdFormat_ExtractsCollectionName()
    {
        // New format: _id is "databaseName/collectionName"
        var doc = new BsonDocument
        {
            { "_id", "myDb/newCol" },
            { "DatabaseName", "myDb" },
            { "Server", "localhost" },
            { "Registration", 1 },
            { "Types", new BsonArray() },
            { "Source", 1 },
        };

        var result = MongoDbCollectionCache.BsonToCollectionInfo(doc, "cfg");

        result.Should().NotBeNull();
        result.CollectionName.Should().Be("newCol");
    }

    [Fact]
    public void BsonToCollectionInfo_UnresolvableTypeName_SetsCollectionTypeNull()
    {
        var doc = new BsonDocument
        {
            { "_id", "db/col" },
            { "CollectionName", "col" },
            { "DatabaseName", "db" },
            { "Server", "localhost" },
            { "Registration", 0 },
            { "Types", new BsonArray() },
            { "Source", 1 },
            { "CollectionTypeName", "Some.Nonexistent.Type, Missing.Assembly" },
        };

        var result = MongoDbCollectionCache.BsonToCollectionInfo(doc, "cfg");

        result.Should().NotBeNull();
        result.CollectionType.Should().BeNull();
    }

    [Fact]
    public void BsonToCollectionInfo_MissingOptionalFields_UsesDefaults()
    {
        var doc = new BsonDocument
        {
            { "_id", "db/col" },
            { "CollectionName", "col" },
            { "DatabaseName", "db" },
            { "Server", "localhost" },
            { "Registration", 0 },
            { "Types", new BsonArray() },
            { "Source", 1 },
        };

        var result = MongoDbCollectionCache.BsonToCollectionInfo(doc, "cfg");

        result.Should().NotBeNull();
        result.Stats.Should().BeNull();
        result.Index.Should().BeNull();
        result.CollectionType.Should().BeNull();
    }
}

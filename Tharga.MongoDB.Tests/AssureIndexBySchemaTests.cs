using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Lockable;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Regression tests for <see cref="AssureIndexMode.BySchema"/>.
///
/// These verify the bug Eplicta reported (2026-05-02): lockable base-class indexes
/// silently fail to be created when the consumer also declares its own indexes.
/// Root cause: <c>UpdateIndicesBySchemaAsync</c> uses <c>Zip</c> to pair
/// <c>CreateIndexModel</c>s with their <c>IndexMeta</c>s, but the two arrays are
/// built in opposite orders (CoreIndices-first vs Indices-first), so the pairing
/// is wrong whenever both are non-empty.
/// </summary>
[Collection("Sequential")]
public class AssureIndexBySchemaTests : MongoDbTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task BySchema_LockableWithConsumerIndices_CreatesAllIndexes()
    {
        // Arrange — mimic Eplicta's HarvesterDocumentRepositoryCollection shape:
        // lockable base (CoreIndices: Lock, LockStatus) + consumer Indices (State).
        SetAssureIndexMode(AssureIndexMode.BySchema);
        var sut = new LockableWithConsumerIndices(MongoDbServiceFactory, DatabaseContext);

        // Act — first access triggers index assurance.
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "anything" });

        // Assert — all defined indexes are present in MongoDB
        var fetchResult = await sut.FetchCollectionAsync();
        var existing = (await fetchResult.Value.Indexes.ListAsync()).ToList()
            .Select(x => x.GetValue("name").AsString)
            .Where(x => !x.StartsWith("_id_"))
            .ToArray();

        existing.Should().Contain("Lock", "the lockable base declares an index named 'Lock'");
        existing.Should().Contain("LockStatus", "the lockable base declares an index named 'LockStatus'");
        existing.Should().Contain("State", "the consumer declares an index named 'State'");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task BySchema_OnlyConsumerIndices_CreatesIndex()
    {
        SetAssureIndexMode(AssureIndexMode.BySchema);
        var sut = new ConsumerIndicesOnly(MongoDbServiceFactory, DatabaseContext);

        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "anything" });

        var fetchResult = await sut.FetchCollectionAsync();
        var existing = (await fetchResult.Value.Indexes.ListAsync()).ToList()
            .Select(x => x.GetValue("name").AsString)
            .Where(x => !x.StartsWith("_id_"))
            .ToArray();

        existing.Should().Contain("Value");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task BySchema_OnlyCoreIndices_CreatesAllLockIndexes()
    {
        SetAssureIndexMode(AssureIndexMode.BySchema);
        var sut = new CoreIndicesOnly(MongoDbServiceFactory, DatabaseContext);

        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "anything" });

        var fetchResult = await sut.FetchCollectionAsync();
        var existing = (await fetchResult.Value.Indexes.ListAsync()).ToList()
            .Select(x => x.GetValue("name").AsString)
            .Where(x => !x.StartsWith("_id_"))
            .ToArray();

        existing.Should().Contain("Lock");
        existing.Should().Contain("LockStatus");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task BySchema_MatchesByName_ForSameDefinedIndexes()
    {
        // Both modes should converge on the same final index set when the indexes
        // have explicit names (which is required by ByName anyway).
        SetAssureIndexMode(AssureIndexMode.ByName);
        var byNameSut = new LockableWithConsumerIndices(MongoDbServiceFactory, DatabaseContext);
        await byNameSut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "x" });
        var byNameFetch = await byNameSut.FetchCollectionAsync();
        var byNameNames = (await byNameFetch.Value.Indexes.ListAsync()).ToList()
            .Select(x => x.GetValue("name").AsString)
            .Where(x => !x.StartsWith("_id_"))
            .OrderBy(x => x)
            .ToArray();

        // New context for a clean BySchema run
        var bySchemaContext = new DatabaseContext { DatabasePart = System.Guid.NewGuid().ToString(), ConfigurationName = "Default" };
        SetAssureIndexMode(AssureIndexMode.BySchema);
        var bySchemaSut = new LockableWithConsumerIndices(MongoDbServiceFactory, bySchemaContext);
        await bySchemaSut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "x" });
        var bySchemaFetch = await bySchemaSut.FetchCollectionAsync();
        var bySchemaNames = (await bySchemaFetch.Value.Indexes.ListAsync()).ToList()
            .Select(x => x.GetValue("name").AsString)
            .Where(x => !x.StartsWith("_id_"))
            .OrderBy(x => x)
            .ToArray();

        bySchemaNames.Should().BeEquivalentTo(byNameNames);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task BySchema_DuplicateIndexNames_Throws()
    {
        SetAssureIndexMode(AssureIndexMode.BySchema);
        var sut = new DuplicateNameIndices(MongoDbServiceFactory, DatabaseContext);

        var act = () => sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "x" });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Indices can only be defined once with the same name*Dup*");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DropCreate_DuplicateIndexNames_Throws()
    {
        SetAssureIndexMode(AssureIndexMode.DropCreate);
        var sut = new DuplicateNameIndices(MongoDbServiceFactory, DatabaseContext);

        var act = () => sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "x" });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Indices can only be defined once with the same name*Dup*");
    }

    // ---------- Test collections ----------

    /// <summary>
    /// Lockable collection with a consumer-declared index. Mirrors Eplicta's
    /// HarvesterDocumentRepositoryCollection shape (lockable base + State index).
    /// </summary>
    private class LockableWithConsumerIndices : LockableRepositoryCollectionBase<LockableTestEntity, ObjectId>
    {
        public LockableWithConsumerIndices(IMongoDbServiceFactory factory, DatabaseContext databaseContext)
            : base(factory, null, databaseContext)
        {
        }

        protected override bool RequireActor => false;

        public override string CollectionName => "BySchemaLockableWithConsumer";

        public override IEnumerable<CreateIndexModel<LockableTestEntity>> Indices =>
        [
            new(Builders<LockableTestEntity>.IndexKeys.Ascending(x => x.Data),
                new CreateIndexOptions { Name = "State" })
        ];
    }

    private class ConsumerIndicesOnly : Disk.DiskRepositoryCollectionBase<TestEntity, ObjectId>
    {
        public ConsumerIndicesOnly(IMongoDbServiceFactory factory, DatabaseContext databaseContext)
            : base(factory, null, databaseContext)
        {
        }

        public override string CollectionName => "BySchemaConsumerOnly";

        public override IEnumerable<CreateIndexModel<TestEntity>> Indices =>
        [
            new(Builders<TestEntity>.IndexKeys.Ascending(x => x.Value),
                new CreateIndexOptions { Name = "Value" })
        ];
    }

    private class CoreIndicesOnly : LockableRepositoryCollectionBase<LockableTestEntity, ObjectId>
    {
        public CoreIndicesOnly(IMongoDbServiceFactory factory, DatabaseContext databaseContext)
            : base(factory, null, databaseContext)
        {
        }

        protected override bool RequireActor => false;

        public override string CollectionName => "BySchemaCoreOnly";
    }

    /// <summary>
    /// Two consumer indexes with the same explicit name. The up-front validation must
    /// throw before any index is created — otherwise the second CreateOneAsync would
    /// fail mid-loop and leave the collection in a partially-indexed state.
    /// </summary>
    private class DuplicateNameIndices : Disk.DiskRepositoryCollectionBase<TestEntity, ObjectId>
    {
        public DuplicateNameIndices(IMongoDbServiceFactory factory, DatabaseContext databaseContext)
            : base(factory, null, databaseContext)
        {
        }

        public override string CollectionName => "BySchemaDuplicateNames";

        public override IEnumerable<CreateIndexModel<TestEntity>> Indices =>
        [
            new(Builders<TestEntity>.IndexKeys.Ascending(x => x.Value),
                new CreateIndexOptions { Name = "Dup" }),
            new(Builders<TestEntity>.IndexKeys.Descending(x => x.Value),
                new CreateIndexOptions { Name = "Dup" })
        ];
    }
}

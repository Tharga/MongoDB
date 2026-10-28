using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Lockable;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

[Collection("Sequential")]
public class IndexLossTests : MongoDbTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task Lockable_CreateOnGet_SurvivesDeletingItsLastDocument()
    {
        var sut = new LockableCreateOnGetCollection(MongoDbServiceFactory, DatabaseContext);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "only" };
        await sut.AddAsync(entity);
        var collection = (await sut.FetchCollectionAsync()).Value;

        await using (var scope = await sut.PickForDeleteAsync(entity.Id))
        {
            await scope.CommitAsync();
        }

        (await CollectionExistsAsync(collection)).Should().BeTrue();
        (await IndexNamesAsync(collection)).Should().Contain(UniqueIndexName);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DropCollection_NextAdd_RecreatesDeclaredIndexes()
    {
        var sut = new DropEmptyCollection(MongoDbServiceFactory, DatabaseContext);
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "first" });
        var collection = (await sut.FetchCollectionAsync()).Value;

        await sut.DropCollectionAsync();
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "second" });

        (await IndexNamesAsync(collection)).Should().Contain(PlainIndexName);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DeleteLastDocument_KeepsTheCollection()
    {
        var sut = new DropEmptyCollection(MongoDbServiceFactory, DatabaseContext);
        var entity = new TestEntity { Id = ObjectId.GenerateNewId(), Value = "only" };
        await sut.AddAsync(entity);
        var collection = (await sut.FetchCollectionAsync()).Value;

        await sut.DeleteOneAsync(entity.Id);

        (await CollectionExistsAsync(collection)).Should().BeTrue();
        (await IndexNamesAsync(collection)).Should().Contain(PlainIndexName);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task FirstUse_OfAnExistingEmptyCollection_KeepsIt()
    {
        var sut = new DropEmptyCollection(MongoDbServiceFactory, DatabaseContext);
        var collection = await MongoDbServiceFactory.GetMongoDbService(() => DatabaseContext).CreateCollectionAsync<TestEntity>(sut.CollectionName);

        await sut.CountAsync(x => true);

        (await CollectionExistsAsync(collection)).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DropIfEmpty_EmptyCollection_DropsAndNextAddRecreatesIndexes()
    {
        var sut = new DropEmptyCollection(MongoDbServiceFactory, DatabaseContext);
        var entity = new TestEntity { Id = ObjectId.GenerateNewId(), Value = "first" };
        await sut.AddAsync(entity);
        var collection = (await sut.FetchCollectionAsync()).Value;
        await sut.DeleteOneAsync(entity.Id);

        var dropped = await sut.DropIfEmptyAsync();
        var existsAfterDrop = await CollectionExistsAsync(collection);
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "second" });

        dropped.Should().BeTrue();
        existsAfterDrop.Should().BeFalse();
        (await IndexNamesAsync(collection)).Should().Contain(PlainIndexName);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DropIfEmpty_WithDocuments_KeepsTheCollection()
    {
        var sut = new DropEmptyCollection(MongoDbServiceFactory, DatabaseContext);
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "only" });
        var collection = (await sut.FetchCollectionAsync()).Value;

        var dropped = await sut.DropIfEmptyAsync();

        dropped.Should().BeFalse();
        (await CollectionExistsAsync(collection)).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DropIfEmpty_WithUniqueIndex_KeepsTheCollection()
    {
        var sut = new DropEmptyUniqueCollection(MongoDbServiceFactory, DatabaseContext);
        var entity = new TestEntity { Id = ObjectId.GenerateNewId(), Value = "only" };
        await sut.AddAsync(entity);
        var collection = (await sut.FetchCollectionAsync()).Value;
        await sut.DeleteOneAsync(entity.Id);

        var dropped = await sut.DropIfEmptyAsync();

        dropped.Should().BeFalse();
        (await CollectionExistsAsync(collection)).Should().BeTrue();
        (await IndexNamesAsync(collection)).Should().Contain(UniqueIndexName);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DropIfEmpty_MissingCollection_ReturnsFalse()
    {
        var sut = new DropEmptyCollection(MongoDbServiceFactory, DatabaseContext);

        var dropped = await sut.DropIfEmptyAsync();

        dropped.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DropIfEmpty_Lockable_DropsAnEmptyCollection()
    {
        var sut = new LockableCollection(MongoDbServiceFactory, DatabaseContext);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "only" };
        await sut.AddAsync(entity);
        var collection = (await sut.FetchCollectionAsync()).Value;
        await using (var scope = await sut.PickForDeleteAsync(entity.Id))
        {
            await scope.CommitAsync();
        }

        var dropped = await sut.DropIfEmptyAsync();

        dropped.Should().BeTrue();
        (await CollectionExistsAsync(collection)).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DropIfEmpty_LockableWithUniqueIndex_KeepsTheCollection()
    {
        var sut = new LockableCreateOnGetCollection(MongoDbServiceFactory, DatabaseContext);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "only" };
        await sut.AddAsync(entity);
        var collection = (await sut.FetchCollectionAsync()).Value;
        await using (var scope = await sut.PickForDeleteAsync(entity.Id))
        {
            await scope.CommitAsync();
        }

        var dropped = await sut.DropIfEmptyAsync();

        dropped.Should().BeFalse();
        (await CollectionExistsAsync(collection)).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DropIndex_DropsOnlyUndeclaredIndexes()
    {
        var sut = new DropEmptyCollection(MongoDbServiceFactory, DatabaseContext);
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "first" });
        var collection = (await sut.FetchCollectionAsync()).Value;
        await collection.Indexes.CreateOneAsync(new CreateIndexModel<TestEntity>(Builders<TestEntity>.IndexKeys.Descending(x => x.Value), new CreateIndexOptions { Name = StrayIndexName }));

        var (before, after) = await sut.DropIndex(collection);

        before.Should().Be(2);
        after.Should().Be(1);
        (await IndexNamesAsync(collection)).Should().BeEquivalentTo("_id_", PlainIndexName);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DropIndex_Lockable_KeepsLockAndDeclaredIndexes()
    {
        var sut = new LockableCreateOnGetCollection(MongoDbServiceFactory, DatabaseContext);
        await sut.AddAsync(new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "first" });
        var collection = (await sut.FetchCollectionAsync()).Value;
        await collection.Indexes.CreateOneAsync(new CreateIndexModel<LockableTestEntity>(Builders<LockableTestEntity>.IndexKeys.Ascending(x => x.Count), new CreateIndexOptions { Name = StrayIndexName }));

        var (before, after) = await sut.DropIndex(collection);

        before.Should().Be(4);
        after.Should().Be(3);
        (await IndexNamesAsync(collection)).Should().BeEquivalentTo("_id_", nameof(LockableEntityBase.Lock), "LockStatus", UniqueIndexName);
    }

    private const string UniqueIndexName = "UniqueData";
    private const string PlainIndexName = "PlainValue";
    private const string StrayIndexName = "Stray";

    private static async Task<bool> CollectionExistsAsync<T>(IMongoCollection<T> collection)
    {
        var filter = new BsonDocument("name", collection.CollectionNamespace.CollectionName);
        var names = await (await collection.Database.ListCollectionNamesAsync(new ListCollectionNamesOptions { Filter = filter })).ToListAsync();
        return names.Count != 0;
    }

    private static async Task<string[]> IndexNamesAsync<T>(IMongoCollection<T> collection)
    {
        var indexes = await (await collection.Indexes.ListAsync()).ToListAsync();
        return indexes.Select(x => x.GetValue("name").AsString).ToArray();
    }

    private class LockableCreateOnGetCollection : LockableRepositoryCollectionBase<LockableTestEntity, ObjectId>
    {
        public LockableCreateOnGetCollection(IMongoDbServiceFactory factory, DatabaseContext databaseContext)
            : base(factory, null, databaseContext)
        {
        }

        protected override bool RequireActor => false;
        public override string CollectionName => "IndexLossLockableCreateOnGet";
        public override CreateStrategy CreateCollectionStrategy => CreateStrategy.CreateOnGet;

        public override IEnumerable<CreateIndexModel<LockableTestEntity>> Indices =>
        [
            new(Builders<LockableTestEntity>.IndexKeys.Ascending(x => x.Data), new CreateIndexOptions { Unique = true, Name = UniqueIndexName })
        ];
    }

    private class LockableCollection : LockableRepositoryCollectionBase<LockableTestEntity, ObjectId>
    {
        public LockableCollection(IMongoDbServiceFactory factory, DatabaseContext databaseContext)
            : base(factory, null, databaseContext)
        {
        }

        protected override bool RequireActor => false;
        public override string CollectionName => "IndexLossLockable";
    }

    private class DropEmptyCollection : DiskRepositoryCollectionBase<TestEntity, ObjectId>
    {
        public DropEmptyCollection(IMongoDbServiceFactory factory, DatabaseContext databaseContext)
            : base(factory, null, databaseContext)
        {
        }

        public override string CollectionName => "IndexLossDropEmpty";
#pragma warning disable CS0618
        public override CreateStrategy CreateCollectionStrategy => CreateStrategy.DropEmpty;
#pragma warning restore CS0618

        public override IEnumerable<CreateIndexModel<TestEntity>> Indices =>
        [
            new(Builders<TestEntity>.IndexKeys.Ascending(x => x.Value), new CreateIndexOptions { Name = PlainIndexName })
        ];
    }

    private class DropEmptyUniqueCollection : DiskRepositoryCollectionBase<TestEntity, ObjectId>
    {
        public DropEmptyUniqueCollection(IMongoDbServiceFactory factory, DatabaseContext databaseContext)
            : base(factory, null, databaseContext)
        {
        }

        public override string CollectionName => "IndexLossDropEmptyUnique";
        public override CreateStrategy CreateCollectionStrategy => CreateStrategy.DropEmpty;

        public override IEnumerable<CreateIndexModel<TestEntity>> Indices =>
        [
            new(Builders<TestEntity>.IndexKeys.Ascending(x => x.Value), new CreateIndexOptions { Unique = true, Name = UniqueIndexName })
        ];
    }
}

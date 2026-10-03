using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
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
    public async Task DropEmpty_WithoutUniqueIndex_DropsAndNextAddRecreatesIndexes()
    {
        var sut = new DropEmptyCollection(MongoDbServiceFactory, DatabaseContext);
        var entity = new TestEntity { Id = ObjectId.GenerateNewId(), Value = "first" };
        await sut.AddAsync(entity);
        var collection = (await sut.FetchCollectionAsync()).Value;

        await sut.DeleteOneAsync(entity.Id);
        var existsAfterDelete = await CollectionExistsAsync(collection);
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "second" });

        existsAfterDelete.Should().BeFalse();
        (await IndexNamesAsync(collection)).Should().Contain(PlainIndexName);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DropEmpty_WithUniqueIndex_KeepsTheCollection()
    {
        var sut = new DropEmptyUniqueCollection(MongoDbServiceFactory, DatabaseContext);
        var entity = new TestEntity { Id = ObjectId.GenerateNewId(), Value = "only" };
        await sut.AddAsync(entity);
        var collection = (await sut.FetchCollectionAsync()).Value;

        await sut.DeleteOneAsync(entity.Id);

        (await CollectionExistsAsync(collection)).Should().BeTrue();
        (await IndexNamesAsync(collection)).Should().Contain(UniqueIndexName);
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

    private class DropEmptyCollection : DiskRepositoryCollectionBase<TestEntity, ObjectId>
    {
        public DropEmptyCollection(IMongoDbServiceFactory factory, DatabaseContext databaseContext)
            : base(factory, null, databaseContext)
        {
        }

        public override string CollectionName => "IndexLossDropEmpty";
        public override CreateStrategy CreateCollectionStrategy => CreateStrategy.DropEmpty;

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

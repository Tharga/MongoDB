using AutoFixture;
using MongoDB.Bson;
using System.Collections.Generic;
using Tharga.MongoDB.Tests.Support;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using System;
using FluentAssertions;
using MongoDB.Driver;
using Tharga.MongoDB.Experimental;

namespace Tharga.MongoDB.Tests.Experimental;

public class BufferTestRepositoryCollection : ReadWriteBufferRepositoryCollectionBase<TestEntity, ObjectId>
{
    public BufferTestRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, null, databaseContext)
    {
    }

    public override string CollectionName => "Test";

    public override IEnumerable<CreateIndexModel<TestEntity>> Indicies => new[]
    {
        new CreateIndexModel<TestEntity>(Builders<TestEntity>.IndexKeys.Ascending(f => f.Value), new CreateIndexOptions { Unique = true, Name = nameof(TestEntity.Value) })
    };

    public override IEnumerable<Type> Types => new[] { typeof(TestSubEntity), typeof(TestEntity) };
}

public class ReadOnlyBufferTestRepositoryCollection : ReadOnlyBufferRepositoryCollectionBase<TestEntity, ObjectId>
{
    public ReadOnlyBufferTestRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, null, databaseContext)
    {
    }

    public override string CollectionName => "Test";

    public override IEnumerable<Type> Types => new[] { typeof(TestSubEntity), typeof(TestEntity) };
}

public class DiskTestRepositoryCollection : ReadWriteDiskRepositoryCollectionBase<TestEntity, ObjectId>
{
    public DiskTestRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, null, databaseContext)
    {
    }

    public override string CollectionName => "Test";
    public override int? ResultLimit => 5;

    public override IEnumerable<CreateIndexModel<TestEntity>> Indicies => new[]
    {
        new CreateIndexModel<TestEntity>(Builders<TestEntity>.IndexKeys.Ascending(f => f.Value), new CreateIndexOptions { Unique = true, Name = nameof(TestEntity.Value) })
    };

    public override IEnumerable<Type> Types => new[] { typeof(TestSubEntity), typeof(TestEntity) };
}

public class ReadOnlyDiskTestRepositoryCollection : ReadOnlyDiskRepositoryCollectionBase<TestEntity, ObjectId>
{
    public ReadOnlyDiskTestRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, null, databaseContext)
    {
    }

    public override string CollectionName => "Test";
    public override int? ResultLimit => 5;

    public override IEnumerable<Type> Types => new[] { typeof(TestSubEntity), typeof(TestEntity) };
}

public abstract class ExperimentalRepositoryCollectionBaseTestBase : ExperimentalMongoDbTestBase
{
    public enum CollectionType
    {
        Disk,
        Buffer,
        ReadOnlyDisk,
        ReadOnlyBuffer,
    }

    private bool _prepared;
    private readonly BufferTestRepositoryCollection _buffer;
    private readonly ReadOnlyBufferTestRepositoryCollection _readOnlyBuffer;
    private readonly DiskTestRepositoryCollection _disk;
    private readonly ReadOnlyDiskTestRepositoryCollection _readOnlyDisk;

    protected ExperimentalRepositoryCollectionBaseTestBase()
    {
        _buffer = new BufferTestRepositoryCollection(MongoDbServiceFactory, DatabaseContext);
        _readOnlyBuffer = new ReadOnlyBufferTestRepositoryCollection(MongoDbServiceFactory, DatabaseContext);
        _disk = new DiskTestRepositoryCollection(MongoDbServiceFactory, DatabaseContext);
        _readOnlyDisk = new ReadOnlyDiskTestRepositoryCollection(MongoDbServiceFactory, DatabaseContext);
    }

    public static IEnumerable<object[]> AllCollectionTypes =>
        new List<object[]>
        {
            new object[] { CollectionType.Disk },
            new object[] { CollectionType.Buffer },
            new object[] { CollectionType.ReadOnlyDisk },
            new object[] { CollectionType.ReadOnlyBuffer },
        };

    public static IEnumerable<object[]> DiskCollectionTypes =>
        new List<object[]>
        {
            new object[] { CollectionType.Disk },
            new object[] { CollectionType.ReadOnlyDisk },
        };

    protected async Task<Tharga.MongoDB.Experimental.RepositoryCollectionBase<TestEntity, ObjectId>> GetCollection(CollectionType collectionType, bool disconnectDisk = false)
    {
        if (!_prepared && InitialData != null && InitialData.Any())
        {
            _prepared = true;
            foreach (var data in InitialData)
            {
                if (!await _disk.AddAsync(data))
                {
                    throw new InvalidOperationException($"Unable to insert item {data.Id}.");
                }
            }
        }

        Tharga.MongoDB.Experimental.RepositoryCollectionBase<TestEntity, ObjectId> sut;
        switch (collectionType)
        {
            case CollectionType.Disk:
                sut = _disk;
                break;
            case CollectionType.Buffer:
                sut = _buffer;
                break;
            case CollectionType.ReadOnlyDisk:
                sut = _readOnlyDisk;
                break;
            case CollectionType.ReadOnlyBuffer:
                sut = _readOnlyBuffer;
                break;
            default:
                throw new ArgumentOutOfRangeException($"Unknown collection type {collectionType}.");
        }

        if (sut is ReadOnlyBufferRepositoryCollectionBase<TestEntity, ObjectId> bufferCollection)
        {
            await bufferCollection.InvalidateBufferAsync();
            if (disconnectDisk)
            {
                await bufferCollection.DisconnectDiskAsync();
            }
        }

        return sut;
    }

    protected TestEntity[] InitialData { get; private set; }

    protected void Prepare(IEnumerable<TestEntity> data)
    {
        InitialData = data.ToArray();
    }

    protected async Task VerifyContentAsync(Tharga.MongoDB.Experimental.RepositoryCollectionBase<TestEntity, ObjectId> sut)
    {
        if (sut is ReadOnlyBufferRepositoryCollectionBase<TestEntity, ObjectId> bufferCollection)
        {
            var allDisk = await bufferCollection.Disk.GetAsync(x => true).ToArrayAsync();
            var allBuffer = await sut.GetAsync(x => true).ToArrayAsync();

            allDisk.Should().HaveSameCount(allBuffer);
            allDisk.Select(x => x.Id).OrderBy(x => x).SequenceEqual(allBuffer.Select(x => x.Id).OrderBy(x => x)).Should().BeTrue();

            foreach (var diskItem in allDisk)
            {
                var bufferItrem = allBuffer.SingleOrDefault(x => x.Id == diskItem.Id);
                diskItem.Should().Be(bufferItrem);
            }
        }
    }
}

public class GetAsyncTest : ExperimentalRepositoryCollectionBaseTestBase
{
    public GetAsyncTest()
    {
        Prepare(new[]
        {
            new Fixture().Build<TestEntity>().With(x => x.Id, ObjectId.GenerateNewId()).Create(),
            new Fixture().Build<TestSubEntity>().With(x => x.Id, ObjectId.GenerateNewId()).Create(),
            new Fixture().Build<TestEntity>().With(x => x.Id, ObjectId.GenerateNewId()).Create()
        });
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(AllCollectionTypes))]
    public async Task Basic(CollectionType collectionType)
    {
        //Arrange
        var sut = await GetCollection(collectionType);

        //Act
        var result = await sut.GetAsync(x => true).ToArrayAsync();

        //Assert
        result.Should().NotBeNull();
        result.Length.Should().Be(3);
        await VerifyContentAsync(sut);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(AllCollectionTypes))]
    public async Task Default(CollectionType collectionType)
    {
        //Arrange
        var sut = await GetCollection(collectionType);

        //Act
        var result = await sut.GetAsync().ToArrayAsync();

        //Assert
        result.Should().NotBeNull();
        result.Length.Should().Be(3);
        await VerifyContentAsync(sut);
    }

    [Theory]
    [Trait("Category", "Database")]
    [MemberData(nameof(DiskCollectionTypes))]
    public async Task BasicWithFilterFromDisk(CollectionType collectionType)
    {
        //Arrange
        var sut = await GetCollection(collectionType) as IReadOnlyDiskRepositoryCollection<TestEntity, ObjectId>;

        //Act
        var filter = Builders<TestEntity>.Filter.Empty;
        var result = await sut.GetAsync(filter).ToArrayAsync();

        //Assert
        result.Should().NotBeNull();
        result.Length.Should().Be(3);
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;

namespace Tharga.MongoDB.Tests.Support;

public abstract class GenericBufferRepositoryCollectionBaseTestBase : MongoDbTestBase
{
    public enum CollectionType
    {
        Disk,
        Buffer
    }

    private readonly RepositoryCollectionBase<TestEntity, ObjectId> _buffer;
    private readonly RepositoryCollectionBase<TestEntity, ObjectId> _disk;
    private bool _prepared;
    protected TestEntity[] InitialData { get; private set; }

    protected GenericBufferRepositoryCollectionBaseTestBase()
    {
        _buffer = new BufferTestRepositoryCollection(MongoDbServiceFactory, DatabaseContext);
        _disk = new DiskTestRepositoryCollection(MongoDbServiceFactory, DatabaseContext);
    }

    public static IEnumerable<object[]> Data =>
        new List<object[]>
        {
            new object[] { CollectionType.Disk },
            new object[] { CollectionType.Buffer }
        };

    protected async Task<RepositoryCollectionBase<TestEntity, ObjectId>> GetCollection(CollectionType collectionType, Func<RepositoryCollectionBase<TestEntity, ObjectId>, Task> action = null, bool disconnectDisk = false)
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

        RepositoryCollectionBase<TestEntity, ObjectId> sut;
        switch (collectionType)
        {
            case CollectionType.Disk:
                sut = _disk;
                break;
            case CollectionType.Buffer:
                sut = _buffer;
                break;
            default:
                throw new ArgumentOutOfRangeException($"Unknown collection type {collectionType}.");
        }

        if (action != null)
        {
            await action.Invoke(sut);
        }

        if (sut is BufferTestRepositoryCollection collection)
        {
            await collection.InvalidateBufferAsync();
            if (disconnectDisk)
            {
                await collection.DisconnectDiskAsync();
            }
        }

        return sut;
    }

    protected void Prepare(IEnumerable<TestEntity> data)
    {
        InitialData = data.ToArray();
    }

    protected async Task VerifyContentAsync(RepositoryCollectionBase<TestEntity, ObjectId> sut)
    {
        if (sut is BufferTestRepositoryCollection collection) await collection.ReconnectDiskAsync();
        (await sut.BaseCollection.GetAsync(x => true).ToArrayAsync()).Should().HaveSameCount(await sut.GetAsync(x => true).ToArrayAsync());
        (await sut.BaseCollection.GetAsync(x => true).ToArrayAsync()).Select(x => x.Id).OrderBy(x => x).ToArray().SequenceEqual((await sut.GetAsync(x => true).ToArrayAsync()).Select(x => x.Id).OrderBy(x => x)).Should().BeTrue();
        await foreach (var item in sut.BaseCollection.GetAsync(x => true))
        {
            var other = await sut.GetOneAsync(item.Id);
            item.Should().Be(other);
        }
    }
}
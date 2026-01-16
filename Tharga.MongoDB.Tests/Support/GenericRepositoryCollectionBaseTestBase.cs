using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB.Tests.Support;

public abstract class GenericRepositoryCollectionBaseTestBase : MongoDbTestBase
{
    [Obsolete("Deprecated")]
    public enum CollectionType
    {
        Disk,
    }

    private readonly DiskRepositoryCollectionBase<TestEntity, ObjectId> _disk;
    private bool _prepared;
    private List<TestEntity> _initialData;
    protected Func<TestEntity>[] InitialDataLoader { get; private set; }
    protected TestEntity[] InitialData
    {
        get
        {
            if (_initialData == null) throw new InvalidOperationException($"Need to call {nameof(GetCollection)} before accessing this property.");
            return _initialData.ToArray();
        }
    }

    protected GenericRepositoryCollectionBaseTestBase()
    {
        _disk = new DiskTestRepositoryCollection(MongoDbServiceFactory, DatabaseContext);
    }

    [Obsolete("Deprecated")]
    public static IEnumerable<object[]> Data =>
        new List<object[]>
        {
            new object[] { CollectionType.Disk }
        };

    protected async Task<DiskRepositoryCollectionBase<TestEntity, ObjectId>> GetCollection(Func<RepositoryCollectionBase<TestEntity, ObjectId>, Task> action = null)
    {
        if (!_prepared && InitialDataLoader != null && InitialDataLoader.Any())
        {
            _initialData = new List<TestEntity>();
            _prepared = true;
            foreach (var data in InitialDataLoader)
            {
                var item = data.Invoke();
                await _disk.AddAsync(item);
                _initialData.Add(item);
            }
        }

        var sut = _disk;
        if (action != null)
        {
            await action.Invoke(sut);
        }

        return sut;
    }

    [Obsolete($"Use {nameof(GetCollection)} without {nameof(CollectionType)} parameter.")]
    protected async Task<RepositoryCollectionBase<TestEntity, ObjectId>> GetCollection(CollectionType collectionType, Func<RepositoryCollectionBase<TestEntity, ObjectId>, Task> action = null, bool disconnectDisk = false)
    {
        if (!_prepared && InitialDataLoader != null && InitialDataLoader.Any())
        {
            _initialData = new List<TestEntity>();
            _prepared = true;
            foreach (var data in InitialDataLoader)
            {
                var item = data.Invoke();
                await _disk.AddAsync(item);
                _initialData.Add(item);
            }
        }

        var sut = _disk;
        if (action != null)
        {
            await action.Invoke(sut);
        }
        return sut;
    }

    protected void Prepare(IEnumerable<Func<TestEntity>> data)
    {
        InitialDataLoader = data.ToArray();
    }

    protected async Task VerifyContentAsync(RepositoryCollectionBase<TestEntity, ObjectId> sut)
    {
        (await sut.BaseCollection.GetAsync(x => true).ToArrayAsync()).Should().HaveSameCount(await sut.GetAsync(x => true).ToArrayAsync());
        (await sut.BaseCollection.GetAsync(x => true).ToArrayAsync()).Select(x => x.Id).OrderBy(x => x).ToArray().SequenceEqual((await sut.GetAsync(x => true).ToArrayAsync()).Select(x => x.Id).OrderBy(x => x)).Should().BeTrue();
        await foreach (var item in sut.BaseCollection.GetAsync(x => true))
        {
            var other = await sut.GetOneAsync(item.Id);
            item.Should().Be(other);
        }
    }
}
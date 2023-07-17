using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using HostSample.Entities;
using MongoDB.Driver;
using Tharga.Toolkit.TypeService;

namespace HostSample.Features.MultiTypeDiskRepo;

public class MyMultiTypeDiskRepo : IMyMultiTypeDiskRepo
{
    private readonly IMyMultiTypeDiskRepoCollection _collection;

    public MyMultiTypeDiskRepo(IMyMultiTypeDiskRepoCollection collection)
    {
        _collection = collection;
    }

    public IAsyncEnumerable<MyEntityBase> GetAll()
    {
        return _collection.GetAsync(x => true);
    }

    public IAsyncEnumerable<T> GetByType<T>() where T : MyEntityBase
    {
        //await foreach (var item in _collection.GetAsync(x => true).Where(x => x.GetType().IsOfType<T>()))
        //await foreach (var item in _collection.GetAsync(x => x.GetType().IsOfType<T>()))
        //var filter = new FilterDefinitionBuilder<MyEntityBase>().OfType<T>();
        //await foreach (var item in _collection.GetAsync(filter))
        //await foreach (var item in _collection.GetAsync<T>(x => true))
        //{
        //    yield return (T)item;
        //}
        throw new NotImplementedException();
    }

    public Task CreateRandom<T>(T item) where T : MyEntityBase
    {
        return _collection.AddAsync(item);
    }
}
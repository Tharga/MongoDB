using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Tharga.MongoDB.Experimental;

public abstract class ReadWriteBufferRepositoryCollectionBase<TEntity, TKey> : ReadOnlyBufferRepositoryCollectionBase<TEntity, TKey>, IBufferRepositoryCollection<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    private GenericReadWriteDiskRepositoryCollection<TEntity, TKey> _readWritedisk;

    protected ReadWriteBufferRepositoryCollectionBase(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<ReadWriteBufferRepositoryCollectionBase<TEntity, TKey>> logger = null, DatabaseContext databaseContext = null)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }

    public virtual bool AutoClean => _mongoDbService.GetAutoClean();
    public virtual bool CleanOnStartup => _mongoDbService.GetCleanOnStartup();
    public virtual bool DropEmptyCollections => _mongoDbService.DropEmptyCollections();
    public virtual IEnumerable<CreateIndexModel<TEntity>> Indicies => null;

    internal override ReadOnlyDiskRepositoryCollectionBase<TEntity, TKey> Disk => _diskConnected ? _readWritedisk ??= new GenericReadWriteDiskRepositoryCollection<TEntity, TKey>(_mongoDbServiceFactory, _databaseContext ?? new DatabaseContext { CollectionName = CollectionName, DatabasePart = DatabasePart, ConfigurationName = ConfigurationName }, _logger, this) : null;

    public Task DropCollectionAsync()
    {
        throw new NotImplementedException();
    }

    public Task<bool> AddAsync(TEntity entity)
    {
        throw new NotImplementedException();
    }

    public Task AddManyAsync(IEnumerable<TEntity> entities)
    {
        throw new NotImplementedException();
    }

    public Task<EntityChangeResult<TEntity>> AddOrReplaceAsync(TEntity entity)
    {
        throw new NotImplementedException();
    }

    public Task<EntityChangeResult<TEntity>> UpdateOneAsync(TKey id, UpdateDefinition<TEntity> update)
    {
        throw new NotImplementedException();
    }

    public Task<TEntity> DeleteOneAsync(TKey id)
    {
        throw new NotImplementedException();
    }

    public Task<TEntity> DeleteOneAsync(Expression<Func<TEntity, bool>> predicate = null, FindOneAndDeleteOptions<TEntity, TEntity> options = default)
    {
        throw new NotImplementedException();
    }

    public Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> predicate)
    {
        throw new NotImplementedException();
    }
}
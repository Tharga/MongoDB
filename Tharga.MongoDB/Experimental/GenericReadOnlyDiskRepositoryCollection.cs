using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Tharga.MongoDB.Experimental;

internal class GenericReadOnlyDiskRepositoryCollection<TEntity, TKey> : ReadOnlyDiskRepositoryCollectionBase<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    private readonly ReadOnlyBufferRepositoryCollectionBase<TEntity, TKey> _buffer;

    public GenericReadOnlyDiskRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger, ReadOnlyBufferRepositoryCollectionBase<TEntity, TKey> buffer)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
        _buffer = buffer;
    }

    internal override string ServerName => _buffer?.ServerName ?? base.ServerName;
    internal override string DatabaseName => _buffer?.DatabaseName ?? base.DatabaseName;
    public override string CollectionName => _buffer?.CollectionName ?? base.CollectionName;
    public override string DatabasePart => _buffer?.DatabasePart ?? base.DatabasePart;
    public override string ConfigurationName => _buffer?.ConfigurationName ?? base.ConfigurationName;
    public override int? ResultLimit => _buffer?.ResultLimit ?? base.ResultLimit;
    public override IEnumerable<Type> Types => _buffer?.Types ?? base.Types;
}
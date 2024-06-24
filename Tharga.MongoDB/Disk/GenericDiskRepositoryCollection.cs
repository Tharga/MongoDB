using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Tharga.MongoDB.Disk;

internal class GenericDiskRepositoryCollection<TEntity, TKey> : DiskRepositoryCollectionBase<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    private readonly RepositoryCollectionBase<TEntity, TKey> _proxy;

    public GenericDiskRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger, RepositoryCollectionBase<TEntity, TKey> proxy)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
        _proxy = proxy;
    }

    internal override string ServerName => _proxy?.ServerName ?? base.ServerName;
    internal override string DatabaseName => _proxy?.DatabaseName ?? base.DatabaseName;
    public override string CollectionName => _proxy?.CollectionName ?? base.CollectionName;
    public override string DatabasePart => _proxy?.DatabasePart ?? base.DatabasePart;
    public override string ConfigurationName => _proxy?.ConfigurationName ?? base.ConfigurationName;
    public override bool AutoClean => _proxy?.AutoClean ?? base.AutoClean;
    public override bool CleanOnStartup => _proxy?.CleanOnStartup ?? base.CleanOnStartup;
    public override bool DropEmptyCollections => _proxy?.DropEmptyCollections ?? base.DropEmptyCollections;
    public override int? ResultLimit => _proxy?.ResultLimit ?? base.ResultLimit;
    public override IEnumerable<CreateIndexModel<TEntity>> Indices => _proxy?.Indices ?? base.Indices;
    internal override IEnumerable<CreateIndexModel<TEntity>> CoreIndices => _proxy?.CoreIndices ?? base.CoreIndices;
    public override IEnumerable<Type> Types => _proxy?.Types ?? base.Types;
}
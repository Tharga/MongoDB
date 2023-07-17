using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Tharga.MongoDB.Buffer;

namespace Tharga.MongoDB.Disk;

internal class GenericDiskRepositoryCollection<TEntity, TKey> : DiskRepositoryCollectionBase<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    private readonly BufferRepositoryCollectionBase<TEntity, TKey> _buffer;

    public GenericDiskRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext, ILogger<RepositoryCollectionBase<TEntity, TKey>> logger, BufferRepositoryCollectionBase<TEntity, TKey> buffer)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
        _buffer = buffer;
    }

    internal override string ServerName => _buffer?.ServerName ?? base.ServerName;
    internal override string DatabaseName => _buffer?.DatabaseName ?? base.DatabaseName;
    public override string CollectionName => _buffer?.CollectionName ?? base.CollectionName;
    public override string DatabasePart => _buffer?.DatabasePart ?? base.DatabasePart;
    public override string ConfigurationName => _buffer?.ConfigurationName ?? base.ConfigurationName;
    public override bool AutoClean => _buffer?.AutoClean ?? base.AutoClean;
    public override bool CleanOnStartup => _buffer?.CleanOnStartup ?? base.CleanOnStartup;
    public override bool DropEmptyCollections => _buffer?.DropEmptyCollections ?? base.DropEmptyCollections;
    public override int? ResultLimit => _buffer?.ResultLimit ?? base.ResultLimit;
    public override IEnumerable<CreateIndexModel<TEntity>> Indicies => _buffer?.Indicies ?? base.Indicies;
    public override IEnumerable<Type> Types => _buffer?.Types ?? base.Types;
}
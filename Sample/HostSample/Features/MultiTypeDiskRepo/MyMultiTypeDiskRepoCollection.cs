using System;
using System.Collections.Generic;
using HostSample.Entities;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;
using Tharga.Toolkit.TypeService;

namespace HostSample.Features.MultiTypeDiskRepo;

public class MyMultiTypeDiskRepoCollection : DiskRepositoryCollectionBase<MyEntityBase, ObjectId>, IMyMultiTypeDiskRepoCollection
{
    private readonly IAssemblyService _assemblyService;

    public MyMultiTypeDiskRepoCollection(IMongoDbServiceFactory mongoDbServiceFactory, IAssemblyService assemblyService, ILogger<MyMultiTypeDiskRepoCollection> logger)
        : base(mongoDbServiceFactory, logger)
    {
        _assemblyService = assemblyService;
    }

    public override IEnumerable<Type> Types => _assemblyService.GetTypes(nameof(MyMultiTypeDiskRepoCollection), x => x.IsOfType<MyEntityBase>() && !x.IsAbstract);

    public override string DatabasePart => "MyDatabasePart";
    public override string CollectionName => "MyMultiCollection";
}
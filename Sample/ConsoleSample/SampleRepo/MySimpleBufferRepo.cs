using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Buffer;
using Tharga.MongoDB.Disk;

namespace ConsoleSample.SampleRepo;

public class MySimpleBufferRepo : BufferRepositoryCollectionBase<MyBaseEntity, ObjectId>, IMyRepo
{
    public MySimpleBufferRepo(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<MySimpleBufferRepo> logger, string collectionName)
        : base(mongoDbServiceFactory, logger, new DatabaseContext { CollectionName = collectionName })
    {
    }

    public override IEnumerable<Type> Types => new[] { typeof(MyEntity), typeof(MyOtherEntity) };
    public override IEnumerable<CreateIndexModel<MyBaseEntity>> Indicies => new[] { new CreateIndexModel<MyBaseEntity>(Builders<MyBaseEntity>.IndexKeys.Ascending(x => x.Value), new CreateIndexOptions { Unique = false, Name = "FarmId" }) };
    public override bool AutoClean => true;
    public override bool CleanOnStartup => true;
}
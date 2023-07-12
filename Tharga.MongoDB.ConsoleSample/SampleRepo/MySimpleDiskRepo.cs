//using System;
//using System.Collections.Generic;
//using Microsoft.Extensions.Logging;
//using MongoDB.Bson;
//using MongoDB.Driver;
//using Tharga.MongoDb.Disk;

//namespace Tharga.MongoDb.ConsoleSample.SampleRepo;

////public class MySimpleDiskRepo : DiskRepositoryCollectionBase<MyBaseEntity, ObjectId>, IMyRepo
////{
////    public MySimpleDiskRepo(IMongoDbService mongoDbService, ILogger<MySimpleDiskRepo> logger)
////        : base(mongoDbService, logger)
////    {
////    }

////    public override string CollectionName => DefaultCollectionName;
////    public override IEnumerable<Type> Types => new[] { typeof(MyEntity), typeof(MyOtherEntity) };
////    public override IEnumerable<CreateIndexModel<MyBaseEntity>> Indicies => new[] { new CreateIndexModel<MyBaseEntity>(Builders<MyBaseEntity>.IndexKeys.Ascending(x => x.Value), new CreateIndexOptions { Unique = false, Name = "FarmId" }) };
////    public override bool AutoClean => true;
////    public override bool CleanOnStartup => true;
////}
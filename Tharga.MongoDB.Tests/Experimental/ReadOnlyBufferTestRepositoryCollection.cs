using System;
using System.Collections.Generic;
using MongoDB.Bson;
using Tharga.MongoDB.Experimental;
using Tharga.MongoDB.Tests.Support;

namespace Tharga.MongoDB.Tests.Experimental;

public class ReadOnlyBufferTestRepositoryCollection : ReadOnlyBufferRepositoryCollectionBase<TestEntity, ObjectId>
{
    public ReadOnlyBufferTestRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, null, databaseContext)
    {
    }

    public override string CollectionName => "Test";

    public override IEnumerable<Type> Types => new[] { typeof(TestSubEntity), typeof(TestEntity) };
}
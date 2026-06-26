using System;
using MongoDB.Driver;

namespace Tharga.MongoDB.Internals;

internal interface IMongoUrlBuilderLoader
{
    (IMongoUrlBuilder Builder, Func<string> ConnectionStringLoader, Func<MongoUrl, MongoUrl> ApplyPoolSizeOverride) GetConnectionStringBuilder(DatabaseContext databaseContext);
}
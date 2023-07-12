using System;

namespace Tharga.MongoDB.Internals;

//TODO: Rename to IMongoUrlBuilderLoader
internal interface IMongoUrlBuilderLoader
{
    (IMongoUrlBuilder Builder, Func<string> ConnectionStringLoader) GetConnectionStringBuilder(DatabaseContext databaseContext);
}
using System;

namespace Tharga.MongoDB.Internals;

internal interface IMongoUrlBuilderLoader
{
    (IMongoUrlBuilder Builder, Func<string> ConnectionStringLoader) GetConnectionStringBuilder(DatabaseContext databaseContext);
}
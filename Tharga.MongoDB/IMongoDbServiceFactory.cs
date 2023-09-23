using System;

namespace Tharga.MongoDB;

public interface IMongoDbServiceFactory
{
    IMongoDbService GetMongoDbService(Func<DatabaseContext> databaseContextLoader);
}
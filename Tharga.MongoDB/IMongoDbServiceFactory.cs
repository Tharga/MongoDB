using System;

namespace Tharga.MongoDB;

public interface IMongoDbServiceFactory
{
    IMongoDbService GetMongoDbService(Func<DatabaseContext> databaseContextLoader);

    /// <summary>
    /// Get IMongoDbService by databasePart. This is the same as to use DatabaseContext.DatabasePart.
    /// </summary>
    /// <param name="databasePart"></param>
    /// <returns></returns>
    IMongoDbService GetMongoDbService(string databasePart);

    IMongoDbService GetMongoDbService();
}
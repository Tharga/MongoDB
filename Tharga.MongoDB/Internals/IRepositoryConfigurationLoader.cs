using System;

namespace Tharga.MongoDB.Internals;

internal interface IRepositoryConfigurationLoader
{
    public IRepositoryConfiguration GetConfiguration(Func<DatabaseContext> databaseContextLoader);
}
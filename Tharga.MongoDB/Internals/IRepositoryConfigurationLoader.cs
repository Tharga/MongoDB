using System;

namespace Tharga.MongoDB.Internals;

internal interface IRepositoryConfigurationLoader
{
    public IRepositoryConfigurationInternal GetConfiguration(Func<DatabaseContext> databaseContextLoader);
}
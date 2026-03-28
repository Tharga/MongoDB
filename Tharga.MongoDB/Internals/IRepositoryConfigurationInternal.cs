using MongoDB.Driver;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Internals;

internal interface IRepositoryConfigurationInternal
{
    ConfigurationName GetConfigurationName();
    DatabaseContext GetDatabaseContext();
    MongoUrl GetDatabaseUrl();
    MongoDbConfig GetConfiguration();
    AssureIndexMode GetAssureIndexMode();
}
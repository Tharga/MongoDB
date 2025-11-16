using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Internals;

internal interface IRepositoryConfigurationInternal
{
    ConfigurationName GetConfigurationName();
    MongoUrl GetDatabaseUrl();
    MongoDbConfig GetConfiguration();
    LogLevel GetExecuteInfoLogLevel();
    bool ShouldAssureIndex();
}
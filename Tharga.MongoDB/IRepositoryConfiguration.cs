using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

public interface IRepositoryConfiguration
{
    string GetRawDatabaseUrl(string configurationName);
    MongoDbConfig GetConfiguration(string configurationName);
}
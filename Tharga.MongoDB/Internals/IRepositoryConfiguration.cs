using MongoDB.Driver;

namespace Tharga.MongoDB.Internals;

public interface IRepositoryConfiguration
{
    MongoUrl GetDatabaseUrl();
    MongoDbConfig GetConfiguration();
}
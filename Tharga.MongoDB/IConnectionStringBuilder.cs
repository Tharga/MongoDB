using MongoDB.Driver;

namespace Tharga.MongoDB;

public interface IMongoUrlBuilder
{
    MongoUrl Build(string connectionString, string databasePart);
}
using MongoDB.Driver;

namespace Tharga.MongoDB;

public interface IMongoUrlBuilder
{
    MongoUrl Build(string connectionString, string databasePart);
}

//public interface IConnectionStringBuilder
//{
//    MongoUrl Build(string connectionString, IDictionary<string, string> variables);
//}
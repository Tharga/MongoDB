using MongoDB.Driver;

namespace Tharga.MongoDB.Internals;

internal interface IMongoDbClientProvider
{
    MongoClient GetClient(MongoUrl mongoUrl);
}
using MongoDB.Driver;

namespace Tharga.MongoDB.Internals;

internal interface IMongoDbServiceInternal : IMongoDbService
{
    IMongoDatabase BaseMongoDatabase { get; }
}

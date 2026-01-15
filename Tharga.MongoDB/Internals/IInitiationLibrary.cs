using System.Collections.Generic;

namespace Tharga.MongoDB.Internals;

public interface IInitiationLibrary
{
    bool ShouldInitiate(string serverName, string databaseName, string collectionName);
    bool ShouldInitiateIndex(string serverName, string databaseName, string collectionName);
    void AddFailedInitiateIndex(string serverName, string databaseName, string collectionName, (IndexFailOperation Drop, string indexName) valueTuple);
    bool RecheckInitiateIndex(string serverName, string databaseName, string collectionName);
    IEnumerable<(IndexFailOperation Operation, string Name)> GetFailedIndices(string serverName, string databaseName, string collectionName);
}
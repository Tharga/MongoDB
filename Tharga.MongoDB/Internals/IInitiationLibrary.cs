using System.Collections.Generic;

namespace Tharga.MongoDB.Internals;

public interface IInitiationLibrary
{
    bool ShouldInitiate(string serverName, string databaseName, string collectionName);
    bool ShouldInitiateIndex(string serverName, string databaseName, string collectionName);

    /// <summary>
    /// Records that the given index operation failed for this collection. Idempotent on
    /// (operation, name); repeated calls overwrite the captured <paramref name="errorMessage"/>
    /// with the latest one so consumers see the most recent reason.
    /// </summary>
    void AddFailedInitiateIndex(string serverName, string databaseName, string collectionName, IndexFailOperation operation, string indexName, string errorMessage);

    /// <summary>
    /// Returns true when this collection has at least one failed index recorded.
    /// </summary>
    bool RecheckInitiateIndex(string serverName, string databaseName, string collectionName);

    /// <summary>
    /// Returns the recorded failures for this collection. Empty when none have been recorded.
    /// </summary>
    IReadOnlyList<IndexFailure> GetFailedIndices(string serverName, string databaseName, string collectionName);
}

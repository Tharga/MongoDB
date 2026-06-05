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

    /// <summary>
    /// Removes the single <c>(operation, indexName)</c> entry from this collection's
    /// failed-index set. Called by the per-index success paths after a successful
    /// <c>CreateOneAsync</c> / <c>DropOneAsync</c> so the in-memory state stays in sync
    /// with reality. No-op when the entry isn't present.
    /// </summary>
    void ClearFailedIndex(string serverName, string databaseName, string collectionName, IndexFailOperation operation, string indexName);

    /// <summary>
    /// Returns the <c>(serverName, databaseName, collectionName)</c> keys of collections
    /// that currently have at least one failed index recorded. Used by
    /// <see cref="IDatabaseMonitor.GetCollectionsWithFailedIndices"/> and the optional
    /// background sweep. Empty in the steady state.
    /// </summary>
    IReadOnlyList<(string ServerName, string DatabaseName, string CollectionName)> GetCollectionsWithFailures();
}

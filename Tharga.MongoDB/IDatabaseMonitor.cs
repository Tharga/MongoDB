using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

public interface IDatabaseMonitor
{
    event EventHandler<CollectionInfoChangedEventArgs> CollectionInfoChangedEvent;
    event EventHandler<CollectionDroppedEventArgs> CollectionDroppedEvent;

    IEnumerable<ConfigurationName> GetConfigurations();
    Task<CollectionInfo> GetInstanceAsync(CollectionFingerprint fingerprint);
    IAsyncEnumerable<CollectionInfo> GetInstancesAsync(bool fullDatabaseScan = false, string filter = default);
    Task RefreshStatsAsync(CollectionFingerprint fingerprint);
    Task TouchAsync(CollectionInfo collectionInfo);
    Task<(int Before, int After)> DropIndexAsync(CollectionInfo collectionInfo);
    Task RestoreIndexAsync(CollectionInfo collectionInfo, bool force);

    /// <summary>
    /// Iterates every known collection (via <see cref="GetInstancesAsync"/>) and calls
    /// <see cref="RestoreIndexAsync"/> on each one. Use to apply newly added indexes
    /// across already-deployed environments without restarting consumer apps.
    /// </summary>
    /// <param name="filter">Optional predicate; collections returning false are skipped.</param>
    /// <param name="progress">Optional progress reporter — fires once per collection.</param>
    /// <param name="cancellationToken">Cancels the iteration between collections.</param>
    Task<IndexAssureSummary> RestoreAllIndicesAsync(
        System.Func<CollectionInfo, bool> filter = null,
        IProgress<IndexAssureProgress> progress = null,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<string[]>> GetIndexBlockersAsync(CollectionInfo collectionInfo, string indexName);
    Task<CleanInfo> CleanAsync(CollectionInfo collectionInfo, bool cleanGuids);
    IEnumerable<CallInfo> GetCalls(CallType callType);

    /// <summary>
    /// Ingest an externally produced call (e.g. from a remote agent) into the monitor pipeline.
    /// The call will appear in GetCalls, summaries, and Blazor components.
    /// </summary>
    void IngestCall(CallDto call);

    void ResetCalls();
    Task ResetAsync();

    // --- API-friendly methods ---

    /// <summary>
    /// Get serialization-friendly representation of calls by type.
    /// </summary>
    IEnumerable<CallDto> GetCallDtos(CallType callType);

    /// <summary>
    /// Resolve the explain plan for a specific call.
    /// </summary>
    Task<string> GetExplainAsync(Guid callKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get call counts per collection fingerprint key.
    /// </summary>
    IReadOnlyDictionary<string, int> GetCallCounts();

    /// <summary>
    /// Get call summary grouped by collection and function (for chatty/slow detection).
    /// </summary>
    IEnumerable<CallSummaryDto> GetCallSummary();

    /// <summary>
    /// Get error summary grouped by exception type and collection.
    /// </summary>
    IEnumerable<ErrorSummaryDto> GetErrorSummary();

    /// <summary>
    /// Get slow calls with index coverage info (for missing index detection).
    /// </summary>
    IAsyncEnumerable<SlowCallWithIndexInfoDto> GetSlowCallsWithIndexInfoAsync();

    /// <summary>
    /// Get aggregate connection pool state.
    /// </summary>
    ConnectionPoolStateDto GetConnectionPoolState();

    // --- Remote client management ---

    /// <summary>
    /// Raised when the list of connected monitoring agents changes.
    /// </summary>
    event EventHandler MonitorClientsChanged;

    /// <summary>
    /// Get all known monitoring agents (connected and recently disconnected).
    /// </summary>
    IEnumerable<MonitorClientDto> GetMonitorClients();

    /// <summary>
    /// Register a connected monitoring agent.
    /// </summary>
    void IngestClientConnected(MonitorClientDto client);

    /// <summary>
    /// Mark a monitoring agent as disconnected.
    /// </summary>
    void IngestClientDisconnected(string connectionId);

    /// <summary>
    /// Ingest collection metadata from a remote agent.
    /// </summary>
    void IngestCollectionInfo(RemoteCollectionInfoDto collectionInfo, string connectionId = null);

    /// <summary>
    /// Get the source names that have reported a given collection (by fingerprint key).
    /// </summary>
    IReadOnlyCollection<string> GetCollectionSources(string fingerprintKey);

    /// <summary>
    /// Find the SignalR connection ID of a connected agent by source name.
    /// Returns null if no connected agent matches.
    /// </summary>
    string FindConnectionIdBySource(string sourceName);

    /// <summary>
    /// Get active subscriptions and their subscriber counts.
    /// Keys are topic names (e.g. "LiveMonitoringMarker"), values are subscriber counts.
    /// </summary>
    IReadOnlyDictionary<string, int> GetSubscriptions();

    /// <summary>
    /// Ingest a queue metric snapshot from a remote agent.
    /// </summary>
    void IngestQueueMetric(string sourceName, int queueCount, int executingCount, double? waitTimeMs);

    /// <summary>
    /// Get per-source queue state for all known sources (local + remote).
    /// </summary>
    IReadOnlyDictionary<string, ConnectionPoolStateDto> GetPerSourceQueueState();
}
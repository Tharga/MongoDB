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
    /// Enumerates the registered collections whose in-process initiation state has at
    /// least one failed index. Used by the optional <c>FailedIndexRecheckService</c>
    /// background sweep (controlled via <c>DatabaseOptions.FailedIndexRecheckInterval</c>)
    /// and available for consumer UIs that want to surface "broken index" badges. Returns
    /// an empty list when no collection has recorded any failure. In-process scope — see
    /// the planned <c>index-failure-persistence</c> follow-up for cross-process state.
    /// </summary>
    IReadOnlyList<CollectionInfo> GetCollectionsWithFailedIndices();

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

    /// <summary>
    /// Whether an action (touch/clean/index) on this collection can currently be serviced — either
    /// directly by this process (it has the collection in code and the configuration) or by a
    /// connected agent that reports it. Returns <c>false</c> for <see cref="Registration.NotInCode"/>
    /// collections and for remote collections whose reporting agents have all disconnected. Used by
    /// UIs to gate action buttons so they aren't offered when every action would throw.
    /// </summary>
    bool CanExecuteActions(CollectionInfo collectionInfo);

    /// <summary>
    /// Fetch a single raw document by id. <paramref name="idRaw"/> is auto-detected as Guid → ObjectId → string.
    /// Returns <c>null</c> when no document matches. Returned <see cref="DocumentDto.Json"/> is MongoDB Extended JSON.
    /// </summary>
    Task<DocumentDto> GetDocumentAsync(CollectionInfo collectionInfo, string idRaw, CancellationToken cancellationToken = default);

    /// <summary>
    /// List up to <see cref="DocumentListQuery.Limit"/> raw documents from the collection.
    /// <see cref="DocumentListQuery.FilterJson"/> and <see cref="DocumentListQuery.SortJson"/> are parsed via
    /// <c>BsonDocument.Parse</c>; invalid JSON throws <see cref="System.FormatException"/>.
    /// </summary>
    Task<DocumentListDto> ListDocumentsAsync(CollectionInfo collectionInfo, DocumentListQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sample up to <paramref name="sampleSize"/> documents and return a three-way diff between the C# entity type's
    /// public properties, the registered entity-type names, and the field set observed in the sample.
    /// Top-level fields only.
    /// </summary>
    Task<SchemaComparisonDto> CompareSchemaAsync(CollectionInfo collectionInfo, int sampleSize, CancellationToken cancellationToken = default);

    IEnumerable<CallInfo> GetCalls(CallType callType);

    /// <summary>
    /// Ingest an externally produced call (e.g. from a remote agent) into the monitor pipeline.
    /// The call will appear in GetCalls, summaries, and Blazor components.
    /// </summary>
    void IngestCall(CallDto call, string connectionId = null);

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
    /// Atomic snapshot of everything a single agent has contributed: its
    /// <see cref="MonitorClientDto"/>, the collections it has reported, its
    /// most recent <paramref name="recentCallLimit"/> calls, and its latest
    /// queue state. Returns <c>null</c> when no agent matches
    /// <paramref name="sourceName"/>. Powers the per-agent detail dialog.
    /// </summary>
    MonitorClientDetail GetMonitorClientDetail(string sourceName, int recentCallLimit = 20);

    /// <summary>
    /// Recent SignalR messages exchanged with a given agent (newest first), for the per-agent
    /// Communication diagnostic view. Bounded, in-memory; empty when monitoring isn't enabled.
    /// </summary>
    IReadOnlyList<CommunicationEvent> GetClientCommunication(string sourceName);

    /// <summary>
    /// Record a SignalR message in the per-agent Communication log. For server-side components
    /// (handlers, dispatcher, subscription service) to surface traffic they send/receive.
    /// </summary>
    void RecordClientCommunication(string sourceName, CommunicationDirection direction, string messageType, string summary);

    /// <summary>
    /// Register a connected monitoring agent.
    /// </summary>
    void IngestClientConnected(MonitorClientDto client);

    /// <summary>
    /// Record an agent's self-reported configuration (call forwarding, queue interval, …), correlated by source name.
    /// </summary>
    void IngestClientStatus(string sourceName, MonitorClientStatus status, string connectionId = null);

    /// <summary>
    /// Turn completed-call forwarding on or off on a connected agent (by source name). The agent
    /// re-reports its status afterward; the returned value is its resulting state. Throws when the
    /// agent isn't connected or remote dispatch isn't available.
    /// </summary>
    Task<bool> SetClientCallForwardingAsync(string sourceName, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Whether driver command monitoring is currently capturing in this process.</summary>
    bool CommandMonitoringEnabled { get; }

    /// <summary>Turn driver command monitoring capture on or off in this process (the listener is always subscribed).</summary>
    void SetCommandMonitoring(bool enabled);

    /// <summary>
    /// Turn command monitoring on or off on a connected agent (by source name). The agent re-reports its
    /// status afterward; the returned value is its resulting state. Throws when the agent isn't connected or
    /// remote dispatch isn't available.
    /// </summary>
    Task<bool> SetClientCommandMonitoringAsync(string sourceName, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark a monitoring agent as disconnected.
    /// </summary>
    void IngestClientDisconnected(string connectionId);

    /// <summary>
    /// Ingest collection metadata from a remote agent.
    /// </summary>
    void IngestCollectionInfo(RemoteCollectionInfoDto collectionInfo, string connectionId = null);

    /// <summary>
    /// Ingest a collection-dropped notification from a remote agent. Removes that agent as a source
    /// for the collection; when no source remains the collection is removed and
    /// <see cref="CollectionDroppedEvent"/> is raised. A collection still reported by another agent
    /// (or reachable locally) survives.
    /// </summary>
    void IngestCollectionDropped(string sourceName, string configurationName, string databaseName, string collectionName, string connectionId = null);

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
    /// Ingest a queue metric snapshot from a remote agent (legacy, aggregate-per-source form).
    /// Stored as a single synthetic pool so pre-per-pool agents still surface a line.
    /// </summary>
    void IngestQueueMetric(string sourceName, int queueCount, int executingCount, double? waitTimeMs, string connectionId = null);

    /// <summary>
    /// Ingest a per-pool queue metric snapshot from a remote agent.
    /// </summary>
    void IngestQueueMetric(string sourceName, IReadOnlyList<PoolMetricDto> pools, string connectionId = null);

    /// <summary>
    /// Get per-connection-pool queue state for all known sources (local + remote). Keyed by a unique
    /// <c>"{source}::{serverKey}"</c> key; each value carries a display <see cref="ConnectionPoolStateDto.Label"/>
    /// (the configuration name(s) routing through that pool).
    /// </summary>
    IReadOnlyDictionary<string, ConnectionPoolStateDto> GetPerPoolQueueState();

    /// <summary>
    /// The calls the local limiter is currently holding (queued or executing) — for diagnosing a flood.
    /// Remote agents are not aggregated here; query each agent's own monitor for its in-flight calls.
    /// </summary>
    IReadOnlyList<InFlightCallInfo> GetInFlightCalls();

    /// <summary>
    /// Per-cluster open-connection totals (and capacity) aggregated across this server and all connected
    /// agents, for comparing against a cluster's connection limit (e.g. Atlas max connections).
    /// </summary>
    IReadOnlyList<ClusterConnectionSummary> GetClusterConnectionSummary();
}
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
}
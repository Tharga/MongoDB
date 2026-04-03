using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

internal class DatabaseNullMonitor : IDatabaseMonitor
{
    public event EventHandler<CollectionInfoChangedEventArgs> CollectionInfoChangedEvent;
    public event EventHandler<CollectionDroppedEventArgs> CollectionDroppedEvent;
    public event EventHandler MonitorClientsChanged { add { } remove { } }

    public IEnumerable<ConfigurationName> GetConfigurations()
    {
        yield break;
    }

    public Task<CollectionInfo> GetInstanceAsync(CollectionFingerprint fingerprint)
    {
        return default;
    }

    public async IAsyncEnumerable<CollectionInfo> GetInstancesAsync(bool fullDatabaseScan, string filter)
    {
        yield break;
    }

    public Task RefreshStatsAsync(CollectionFingerprint fingerprint)
    {
        return Task.CompletedTask;
    }

    public Task TouchAsync(CollectionInfo collectionInfo)
    {
        return Task.CompletedTask;
    }

    public Task<(int Before, int After)> DropIndexAsync(CollectionInfo collectionInfo)
    {
        return Task.FromResult((0, 0));
    }

    public Task RestoreIndexAsync(CollectionInfo collectionInfo, bool force)
    {
        return Task.CompletedTask;
    }

    public Task<IEnumerable<string[]>> GetIndexBlockersAsync(CollectionInfo collectionInfo, string indexName)
    {
        return Task.FromResult<IEnumerable<string[]>>(new List<string[]>());
    }

    public Task<CleanInfo> CleanAsync(CollectionInfo collectionInfo, bool cleanGuids)
    {
        return Task.FromResult<CleanInfo>(null);
    }

    public IEnumerable<CallInfo> GetCalls(CallType callType)
    {
        yield break;
    }

    public void IngestCall(CallDto call) { }

    public IEnumerable<MonitorClientDto> GetMonitorClients() { yield break; }

    public void IngestClientConnected(MonitorClientDto client) { }

    public void IngestClientDisconnected(string connectionId) { }

    public void IngestCollectionInfo(RemoteCollectionInfoDto collectionInfo, string connectionId = null) { }

    public IReadOnlyCollection<string> GetCollectionSources(string fingerprintKey) => [];

    public string FindConnectionIdBySource(string sourceName) => null;

    public IReadOnlyDictionary<string, int> GetSubscriptions() => new Dictionary<string, int>();

    public void IngestQueueMetric(string sourceName, int queueCount, int executingCount, double? waitTimeMs) { }

    public IReadOnlyDictionary<string, ConnectionPoolStateDto> GetPerSourceQueueState() => new Dictionary<string, ConnectionPoolStateDto>();

    public void ResetCalls() { }

    public Task ResetAsync() => Task.CompletedTask;

    public IEnumerable<CallDto> GetCallDtos(CallType callType) { yield break; }

    public Task<string> GetExplainAsync(Guid callKey, CancellationToken cancellationToken = default) => Task.FromResult<string>(null);

    public IReadOnlyDictionary<string, int> GetCallCounts() => new Dictionary<string, int>();

    public IEnumerable<CallSummaryDto> GetCallSummary() { yield break; }

    public IEnumerable<ErrorSummaryDto> GetErrorSummary() { yield break; }

    public async IAsyncEnumerable<SlowCallWithIndexInfoDto> GetSlowCallsWithIndexInfoAsync() { yield break; }

    public ConnectionPoolStateDto GetConnectionPoolState() => new()
    {
        QueueCount = 0,
        ExecutingCount = 0,
        LastWaitTimeMs = 0,
        RecentMetrics = []
    };
}
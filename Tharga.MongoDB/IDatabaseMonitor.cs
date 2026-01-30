using System;
using System.Collections.Generic;
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
    Task TouchAsync(CollectionInfo collectionInfo);
    Task<(int Before, int After)> DropIndexAsync(CollectionInfo collectionInfo);
    Task RestoreIndexAsync(CollectionInfo collectionInfo);
    Task<IEnumerable<string[]>> GetIndexBlockersAsync(CollectionInfo collectionInfo, string indexName);
    IEnumerable<CallInfo> GetCalls(CallType callType);
}
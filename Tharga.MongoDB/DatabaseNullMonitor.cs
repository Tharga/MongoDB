using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

internal class DatabaseNullMonitor : IDatabaseMonitor
{
    public event EventHandler<CollectionInfoChangedEventArgs> CollectionInfoChangedEvent;
    public event EventHandler<CollectionDroppedEventArgs> CollectionDroppedEvent;

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

    public Task TouchAsync(CollectionInfo collectionInfo)
    {
        return Task.CompletedTask;
    }

    public Task<(int Before, int After)> DropIndexAsync(CollectionInfo collectionInfo)
    {
        return Task.FromResult((0, 0));
    }

    public Task RestoreIndexAsync(CollectionInfo collectionInfo)
    {
        return Task.CompletedTask;
    }

    public Task<IEnumerable<string[]>> GetIndexBlockersAsync(CollectionInfo collectionInfo, string indexName)
    {
        return Task.FromResult<IEnumerable<string[]>>(new List<string[]>());
    }

    public IEnumerable<CallInfo> GetCalls(CallType callType)
    {
        yield break;
    }
}
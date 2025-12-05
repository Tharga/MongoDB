using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

public interface IDatabaseMonitor
{
    //event EventHandler<IndexUpdatedEventArgs> IndexUpdatedEvent;
    //event EventHandler<CollectionAccessEventArgs> CollectionAccessEvent;
    event EventHandler<CollectionInfoChangedEventArgs> CollectionInfoChangedEvent;

    IEnumerable<ConfigurationName> GetConfigurations();
    Task<CollectionInfo> GetInstanceAsync(CollectionFingerprint fingerprint);
    IAsyncEnumerable<CollectionInfo> GetInstancesAsync(bool fullDatabaseScan = false);
    Task TouchAsync(DatabaseContext databaseContext, Type collectionType, Registration registration); //TODO: Provide CollectionInfo as parameter instead.
    Task<(int Before, int After)> DropIndexAsync(DatabaseContext databaseContext, Type collectionType, Registration registration); //TODO: Provide CollectionInfo as parameter instead.
    Task RestoreIndexAsync(DatabaseContext databaseContext, Type collectionType, Registration registration); //TODO: Provide CollectionInfo as parameter instead.
    IEnumerable<CallInfo> GetCalls(CallType callType);
}
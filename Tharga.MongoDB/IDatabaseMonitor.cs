using System.Collections.Generic;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

public interface IDatabaseMonitor
{
    IEnumerable<ConfigurationName> GetConfigurations();
    Task<CollectionInfo> GetInstanceAsync(CollectionFingerprint fingerprint);
    IAsyncEnumerable<CollectionInfo> GetInstancesAsync(bool fullDatabaseScan = false);
    Task<(int Before, int After)> DropIndexAsync(IDatabaseContext context);
    Task RestoreIndexAsync(IDatabaseContext context);
    Task TouchAsync(IDatabaseContext context);
    IEnumerable<CallInfo> GetCalls(CallType callType);
}
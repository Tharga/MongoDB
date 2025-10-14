using System.Collections.Generic;

namespace Tharga.MongoDB;

public interface IDatabaseMonitor
{
    IAsyncEnumerable<CollectionInfo> GetInstancesAsync(DatabaseContext databaseContext = null);
}
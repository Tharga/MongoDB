using System.Collections.Generic;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

public interface IDatabaseMonitor
{
    IEnumerable<ConfigurationName> GetConfigurations();
    IAsyncEnumerable<CollectionInfo> GetInstancesAsync();
    Task DropIndexAsync(DatabaseContext databaseContext);
    Task RestoreIndexAsync(DatabaseContext databaseContext);
}
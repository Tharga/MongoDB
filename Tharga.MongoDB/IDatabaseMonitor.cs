using System.Collections.Generic;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

public interface IDatabaseMonitor
{
    IEnumerable<ConfigurationName> GetConfigurations();
    IAsyncEnumerable<CollectionInfo> GetInstancesAsync(ConfigurationName configurationName = null);
}
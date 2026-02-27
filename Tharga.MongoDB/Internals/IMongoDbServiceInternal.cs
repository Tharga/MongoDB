using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tharga.MongoDB.Internals;

internal interface IMongoDbServiceInternal : IMongoDbService
{
    Task<MonitorRecord> ReadMonitorRecordAsync(string databaseName, string collectionName);
    Task<IEnumerable<MonitorRecord>> GetAllMonitorRecordsAsync(string databaseName);
    Task SaveMonitorRecordAsync(string databaseName, MonitorRecord record);
    Task RemoveMonitorRecordAsync(string databaseName, string collectionName);
}

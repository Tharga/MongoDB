using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

public interface IMongoDbFirewallService
{
    Task<FirewallResponse> AssureFirewallAccessAsync(MongoDbApiAccess access, string name = null);
    Task<(bool HaveAccess, string Message)> HaveAccess(MongoDbApiAccess access);
    IAsyncEnumerable<WhiteListItem> GetFirewallListAsync(MongoDbApiAccess access);
    Task RemoveFromFirewallAsync(MongoDbApiAccess access, string name);
    Task AddToFirewallAsync(MongoDbApiAccess access, string name, IPAddress ipAddress);
}
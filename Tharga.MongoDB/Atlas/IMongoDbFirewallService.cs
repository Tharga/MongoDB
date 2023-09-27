using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

internal interface IMongoDbFirewallService
{
    Task<FirewallResponse> AssureFirewallAccessAsync(MongoDbApiAccess access, string name = null);
    IAsyncEnumerable<WhiteListItem> GetFirewallListAsync(MongoDbApiAccess access);
    Task RemoveFromFirewallAsync(MongoDbApiAccess access, string name);
    Task AddToFirewallAsync(MongoDbApiAccess access, string name, IPAddress ipAddress);
}
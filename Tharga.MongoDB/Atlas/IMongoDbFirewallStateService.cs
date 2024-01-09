using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

internal interface IMongoDbFirewallStateService
{
    ValueTask<string> AssureFirewallAccessAsync(MongoDbApiAccess accessInfo, bool force = false);
}
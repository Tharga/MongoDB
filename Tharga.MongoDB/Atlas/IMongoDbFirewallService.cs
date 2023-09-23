using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

internal interface IMongoDbFirewallService
{
    Task<bool> OpenMongoDbFirewall(MongoDbApiAccess mongoDbApiAccess);
}
using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

internal class MongoDbFirewallService : IMongoDbFirewallService
{
    private readonly ILogger<MongoDbFirewallService> _logger;
    private IPAddress _ipAddress;

    public MongoDbFirewallService(ILogger<MongoDbFirewallService> logger)
    {
        _logger = logger;
    }

    public async Task OpenMongoDbFirewallAsync(MongoDbApiAccess mongoDbApiAccess)
    {
        if (!string.IsNullOrEmpty($"{_ipAddress}")) return;
        if (string.IsNullOrEmpty(mongoDbApiAccess?.PublicKey) || string.IsNullOrEmpty(mongoDbApiAccess.PrivateKey) || string.IsNullOrEmpty(mongoDbApiAccess.GroupId))
        {
            _logger.LogTrace("No firewall configuration, cannot open MongoDB Atlas firewall.");
            return;
        }

        var service = new AtlasAdministrationService(mongoDbApiAccess, _logger);
        //TODO: Have a way to provide name
        _ipAddress = await service.AssureAccess($"{Environment.MachineName}-Auto");
    }
}
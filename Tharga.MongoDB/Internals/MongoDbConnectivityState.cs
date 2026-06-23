using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Internals;

internal class MongoDbConnectivityState : IMongoDbConnectivityState
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(1);

    private readonly IMongoDbServiceFactory _factory;
    private readonly IRepositoryConfiguration _repositoryConfiguration;
    private readonly ILogger<MongoDbConnectivityState> _logger;
    private volatile IReadOnlyList<ConnectionConnectivity> _connections = Array.Empty<ConnectionConnectivity>();

    public MongoDbConnectivityState(IMongoDbServiceFactory factory, IRepositoryConfiguration repositoryConfiguration, ILogger<MongoDbConnectivityState> logger)
    {
        _factory = factory;
        _repositoryConfiguration = repositoryConfiguration;
        _logger = logger;
    }

    public bool IsHealthy => _connections.All(c => c.CanConnect);

    public IReadOnlyList<ConnectionConnectivity> Connections => _connections;

    public Task<IReadOnlyList<ConnectionConnectivity>> CheckAsync(CancellationToken cancellationToken = default)
        => CheckWithRetryAsync(attempts: 1, initialDelay: TimeSpan.Zero, assureFirewall: true, cancellationToken);

    /// <summary>
    /// Probes every configured connection, retrying the still-unreachable ones up to
    /// <paramref name="attempts"/> times with exponential backoff starting at
    /// <paramref name="initialDelay"/>. Healthy connections are not re-probed. Used by the
    /// startup pre-check; never throws.
    /// </summary>
    internal async Task<IReadOnlyList<ConnectionConnectivity>> CheckWithRetryAsync(int attempts, TimeSpan initialDelay, bool assureFirewall, CancellationToken cancellationToken = default)
    {
        if (attempts < 1) attempts = 1;

        var configNames = _repositoryConfiguration.GetDatabaseConfigurationNames().ToArray();
        var results = new Dictionary<string, ConnectionConnectivity>();
        var delay = initialDelay;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            if (attempt > 1 && delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxBackoff.Ticks));
            }

            foreach (var configName in configNames)
            {
                if (results.TryGetValue(configName, out var existing) && existing.CanConnect) continue;
                results[configName] = await ProbeAsync(configName, assureFirewall);
            }

            if (configNames.All(c => results.TryGetValue(c, out var r) && r.CanConnect)) break;
            if (cancellationToken.IsCancellationRequested) break;
        }

        var ordered = configNames.Select(c => results[c]).ToArray();
        _connections = ordered;
        return ordered;
    }

    private async Task<ConnectionConnectivity> ProbeAsync(string configName, bool assureFirewall)
    {
        try
        {
            var svc = _factory.GetMongoDbService(() => new DatabaseContext { ConfigurationName = configName });
            var info = await svc.GetInfoAsync(assureFirewall);
            return new ConnectionConnectivity
            {
                ConfigurationName = configName,
                CanConnect = info.CanConnect,
                Message = info.Message,
                Firewall = info.Firewall,
                CheckedAt = DateTime.UtcNow,
            };
        }
        catch (Exception ex)
        {
            // GetInfoAsync is non-throwing, but resolving the service / assuring the firewall can
            // still throw. A probe must never propagate — that is the whole point of this type.
            _logger.LogWarning(ex, "Connectivity probe for configuration '{ConfigName}' threw.", configName);
            return new ConnectionConnectivity
            {
                ConfigurationName = configName,
                CanConnect = false,
                Message = ex.Message,
                CheckedAt = DateTime.UtcNow,
            };
        }
    }
}

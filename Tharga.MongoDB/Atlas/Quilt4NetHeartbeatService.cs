using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

/// <summary>
/// Background heartbeat for Quilt4Net firewall openings. Wakes on
/// <see cref="DatabaseOptions.Quilt4NetHeartbeatInterval"/>; per active
/// <c>(MongoDbApiAccess, IPAddress)</c> tuple, calls the right proxy endpoint:
/// <c>OpenAsync</c> for Open mode (doubles as usage signal when already open) or
/// <c>ReportUsedAsync</c> for Notify mode. Dormant when no entries are registered.
/// Auth-rejected entries are removed; transient failures keep the entry.
/// </summary>
internal sealed class Quilt4NetHeartbeatService : BackgroundService
{
    private readonly Quilt4NetFirewallService _firewall;
    private readonly ILogger<Quilt4NetHeartbeatService> _logger;
    private readonly TimeSpan? _interval;
    private readonly ConcurrentDictionary<(MongoDbApiAccess access, IPAddress ip), FirewallMode> _active = new();

    public Quilt4NetHeartbeatService(
        Quilt4NetFirewallService firewall,
        IOptions<DatabaseOptions> options,
        ILogger<Quilt4NetHeartbeatService> logger)
    {
        _firewall = firewall;
        _logger = logger;
        _interval = options.Value.Quilt4NetHeartbeatInterval;
    }

    public void Register(MongoDbApiAccess access, IPAddress ip, FirewallMode mode)
    {
        if (mode != FirewallMode.Notify && mode != FirewallMode.Open) return;
        _active[(access, ip)] = mode;
    }

    public void Unregister(MongoDbApiAccess access, IPAddress ip)
    {
        _active.TryRemove((access, ip), out _);
    }

    public int ActiveCount => _active.Count;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_interval is null) return; // Disabled.

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(_interval.Value, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            if (_active.IsEmpty) continue; // Dormant — no log noise.

            foreach (var ((access, ip), mode) in _active)
            {
                await BeatOneAsync(access, ip, mode, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task BeatOneAsync(MongoDbApiAccess access, IPAddress ip, FirewallMode mode, CancellationToken ct)
    {
        try
        {
            if (mode == FirewallMode.Open)
            {
                // OpenAsync also serves as the usage signal when the firewall is already open
                // (returns AlreadyOpen). No separate ReportUsedAsync needed in Open mode.
                await _firewall.OpenAsync(access, ip, name: null, ct).ConfigureAwait(false);
            }
            else
            {
                await _firewall.ReportUsedAsync(access, ip, ct).ConfigureAwait(false);
            }
        }
        catch (Quilt4NetFirewallAuthorizationException ex)
        {
            _logger.LogWarning(ex,
                "Quilt4Net heartbeat: auth rejected for {Group}/{Ip} — removing from heartbeat loop.",
                access.GroupId, ip);
            _active.TryRemove((access, ip), out _);
        }
        catch (OperationCanceledException) { /* stoppingToken — exit cleanly */ }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Quilt4Net heartbeat: transient failure for {Group}/{Ip}; keeping entry.",
                access.GroupId, ip);
        }
    }
}

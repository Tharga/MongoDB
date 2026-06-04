using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Internals;

/// <summary>
/// Periodically re-attempts any failed index across all registered collections.
/// Registered from <c>UseMongoDB</c> only when <see cref="DatabaseOptions.FailedIndexRecheckInterval"/>
/// is non-null. Idle when healthy — each tick first asks <c>IDatabaseMonitor.GetCollectionsWithFailedIndices</c>
/// and returns immediately when the result is empty, so a steady-state app pays no cost.
/// </summary>
internal sealed class FailedIndexRecheckService : BackgroundService
{
    private readonly IDatabaseMonitor _databaseMonitor;
    private readonly TimeSpan _interval;
    private readonly ILogger<FailedIndexRecheckService> _logger;

    public FailedIndexRecheckService(IDatabaseMonitor databaseMonitor, IOptions<DatabaseOptions> options, ILogger<FailedIndexRecheckService> logger = null)
    {
        _databaseMonitor = databaseMonitor;
        _interval = options.Value.FailedIndexRecheckInterval ?? TimeSpan.FromHours(1);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                var failed = _databaseMonitor.GetCollectionsWithFailedIndices();
                if (failed.Count == 0) continue;

                foreach (var info in failed)
                {
                    if (stoppingToken.IsCancellationRequested) return;
                    try
                    {
                        await _databaseMonitor.RestoreIndexAsync(info, force: false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "Failed-index recheck sweep: retry threw for {Configuration}.{Database}.{Collection}.",
                            info.ConfigurationName, info.DatabaseName, info.CollectionName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed-index recheck sweep tick threw — will retry on next interval.");
            }
        }
    }
}

using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MongoDB.Driver.Core.Events;

namespace Tharga.MongoDB.Internals;

internal class CommandMonitorService : ICommandMonitorService
{
    private const int MaxEntries = 2000;
    private readonly ILogger<CommandMonitorService> _logger;
    private readonly ConcurrentDictionary<int, CommandEntry> _entries = new();
    private readonly ConcurrentQueue<int> _order = new();

    public CommandMonitorService(ILogger<CommandMonitorService> logger)
    {
        _logger = logger;
    }

    public void OnCommandSucceeded(CommandSucceededEvent e)
    {
        var entry = new CommandEntry
        {
            CommandName = e.CommandName,
            Duration = e.Duration,
            DatabaseNamespace = e.DatabaseNamespace?.DatabaseName,
        };

        Store(e.RequestId, entry);

        _logger?.LogDebug("Command {CommandName} on {Database} completed in {Duration}ms (RequestId: {RequestId}).",
            e.CommandName, entry.DatabaseNamespace, e.Duration.TotalMilliseconds, e.RequestId);
    }

    public void OnCommandFailed(CommandFailedEvent e)
    {
        var entry = new CommandEntry
        {
            CommandName = e.CommandName,
            Duration = e.Duration,
            DatabaseNamespace = e.DatabaseNamespace?.DatabaseName,
            Failed = true,
        };

        Store(e.RequestId, entry);

        _logger?.LogDebug("Command {CommandName} on {Database} failed after {Duration}ms (RequestId: {RequestId}).",
            e.CommandName, entry.DatabaseNamespace, e.Duration.TotalMilliseconds, e.RequestId);
    }

    public CommandEntry TakeLatestForCurrentThread()
    {
        // Drain entries older than 5 seconds during lookup
        while (_order.TryPeek(out var oldId) && _entries.TryGetValue(oldId, out var old) && old.Timestamp < DateTime.UtcNow.AddSeconds(-5))
        {
            if (_order.TryDequeue(out var removed))
                _entries.TryRemove(removed, out _);
        }

        // Find the most recent entry — command events fire on the same connection,
        // so we take the latest entry by timestamp within a short window.
        CommandEntry latest = null;
        int latestId = -1;
        foreach (var kvp in _entries)
        {
            if (latest == null || kvp.Value.Timestamp > latest.Timestamp)
            {
                latest = kvp.Value;
                latestId = kvp.Key;
            }
        }

        if (latest != null && latestId >= 0)
        {
            _entries.TryRemove(latestId, out _);
        }

        return latest;
    }

    private void Store(int requestId, CommandEntry entry)
    {
        _entries[requestId] = entry;
        _order.Enqueue(requestId);

        while (_order.Count > MaxEntries)
        {
            if (_order.TryDequeue(out var old))
                _entries.TryRemove(old, out _);
        }
    }

    internal record CommandEntry
    {
        public required string CommandName { get; init; }
        public required TimeSpan Duration { get; init; }
        public string DatabaseNamespace { get; init; }
        public bool Failed { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }
}

internal interface ICommandMonitorService
{
    CommandMonitorService.CommandEntry TakeLatestForCurrentThread();
}

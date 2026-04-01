using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using MongoDB.Driver.Core.Events;

namespace Tharga.MongoDB.Internals;

internal class CommandMonitorService : ICommandMonitorService
{
    private const int MaxEntries = 2000;
    private readonly ILogger<CommandMonitorService> _logger;
    private readonly ConcurrentQueue<CommandEntry> _entries = new();

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
            StopwatchTimestamp = Stopwatch.GetTimestamp(),
        };

        Store(entry);

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
            StopwatchTimestamp = Stopwatch.GetTimestamp(),
        };

        Store(entry);

        _logger?.LogDebug("Command {CommandName} on {Database} failed after {Duration}ms (RequestId: {RequestId}).",
            e.CommandName, entry.DatabaseNamespace, e.Duration.TotalMilliseconds, e.RequestId);
    }

    public CommandEntry[] TakeSince(long sinceTimestamp)
    {
        var result = new List<CommandEntry>();
        var remaining = new List<CommandEntry>();

        // Drain the queue and split into matching and remaining
        while (_entries.TryDequeue(out var entry))
        {
            if (entry.StopwatchTimestamp >= sinceTimestamp)
                result.Add(entry);
            else if (entry.StopwatchTimestamp >= sinceTimestamp - Stopwatch.Frequency * 5) // keep entries up to 5s old
                remaining.Add(entry);
            // else: drop old entries
        }

        // Re-enqueue non-matching entries
        foreach (var entry in remaining)
            _entries.Enqueue(entry);

        return result.ToArray();
    }

    private void Store(CommandEntry entry)
    {
        _entries.Enqueue(entry);

        while (_entries.Count > MaxEntries)
            _entries.TryDequeue(out _);
    }

    internal record CommandEntry
    {
        public required string CommandName { get; init; }
        public required TimeSpan Duration { get; init; }
        public string DatabaseNamespace { get; init; }
        public bool Failed { get; init; }
        public long StopwatchTimestamp { get; init; }
    }
}

internal interface ICommandMonitorService
{
    CommandMonitorService.CommandEntry[] TakeSince(long sinceTimestamp);
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using MongoDB.Bson;
using MongoDB.Driver.Core.Events;
using Tharga.MongoDB.Diagnostics;

namespace Tharga.MongoDB.Internals;

/// <summary>
/// Turns driver command events into client dependency spans on the <c>Tharga.MongoDB</c> activity source.
/// The span starts on <see cref="CommandStartedEvent"/> so it inherits the ambient activity as its parent,
/// which is what places a database call inside the request that made it.
/// </summary>
internal sealed class CommandActivityEmitter
{
    private const string DbSystemTag = "db.system";
    private const string DbNameTag = "db.name";
    private const string DbOperationTag = "db.operation";
    private const string DbCollectionTag = "db.mongodb.collection";
    private const string DbStatementTag = "db.statement";
    private const string ServerAddressTag = "server.address";
    private const string ServerPortTag = "server.port";
    private const string ErrorTypeTag = "error.type";
    private const string MongoDbSystem = "mongodb";

    private static readonly ActivitySource ActivitySource = new(MongoDbDiagnostics.ActivitySourceName);

    private static readonly HashSet<string> ExcludedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "hello",
        "isMaster",
        "buildInfo",
        "ping",
        "saslStart",
        "saslContinue",
        "authenticate",
        "getLastError",
        "endSessions"
    };

    private readonly ConcurrentDictionary<int, Activity> _inFlight = new();
    private readonly bool _captureCommandText;

    public CommandActivityEmitter(bool captureCommandText = false)
    {
        _captureCommandText = captureCommandText;
    }

    public void OnCommandStarted(CommandStartedEvent e)
    {
        if (!ActivitySource.HasListeners()) return;
        if (ExcludedCommands.Contains(e.CommandName)) return;

        var databaseName = e.DatabaseNamespace?.DatabaseName;
        var collectionName = GetCollectionName(e.Command);

        var activity = ActivitySource.StartActivity(BuildSpanName(e.CommandName, databaseName, collectionName), ActivityKind.Client);
        if (activity == null) return;

        activity.SetTag(DbSystemTag, MongoDbSystem);
        activity.SetTag(DbNameTag, databaseName);
        activity.SetTag(DbOperationTag, e.CommandName);
        if (collectionName != null) activity.SetTag(DbCollectionTag, collectionName);
        if (_captureCommandText) activity.SetTag(DbStatementTag, e.Command?.ToString());
        SetServerTags(activity, e.ConnectionId?.ServerId?.EndPoint);

        _inFlight[e.RequestId] = activity;
    }

    public void OnCommandSucceeded(CommandSucceededEvent e)
    {
        if (!_inFlight.TryRemove(e.RequestId, out var activity)) return;

        activity.Dispose();
    }

    public void OnCommandFailed(CommandFailedEvent e)
    {
        if (!_inFlight.TryRemove(e.RequestId, out var activity)) return;

        activity.SetStatus(ActivityStatusCode.Error, e.Failure?.Message);
        activity.SetTag(ErrorTypeTag, e.Failure?.GetType().FullName);
        activity.Dispose();
    }

    private static string BuildSpanName(string commandName, string databaseName, string collectionName)
    {
        if (databaseName == null) return commandName;
        return collectionName == null
            ? $"{commandName} {databaseName}"
            : $"{commandName} {databaseName}.{collectionName}";
    }

    /// <summary>
    /// A collection command names its collection in the first element, whose name is the command itself
    /// (<c>{ find: "user", ... }</c>). Database-level commands put a number there instead, which is how a
    /// missing collection identifies itself.
    /// </summary>
    private static string GetCollectionName(BsonDocument command)
    {
        if (command == null || command.ElementCount == 0) return null;

        return command.GetElement(0).Value is BsonString value ? value.Value : null;
    }

    private static void SetServerTags(Activity activity, EndPoint endPoint)
    {
        switch (endPoint)
        {
            case DnsEndPoint dns:
                activity.SetTag(ServerAddressTag, dns.Host);
                activity.SetTag(ServerPortTag, dns.Port);
                break;
            case IPEndPoint ip:
                activity.SetTag(ServerAddressTag, ip.Address.ToString());
                activity.SetTag(ServerPortTag, ip.Port);
                break;
        }
    }
}

using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Tharga.Mcp;

namespace Tharga.MongoDB.Mcp;

/// <summary>
/// Exposes Tharga.MongoDB monitoring data as MCP resources on the System scope.
/// </summary>
public sealed class MongoDbResourceProvider : IMcpResourceProvider
{
    private const string CollectionsUri = "mongodb://collections";
    private const string MonitoringUri = "mongodb://monitoring";
    private const string ClientsUri = "mongodb://clients";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly IDatabaseMonitor _monitor;

    public MongoDbResourceProvider(IDatabaseMonitor monitor)
    {
        _monitor = monitor;
    }

    public McpScope Scope => McpScope.System;

    public Task<IReadOnlyList<McpResourceDescriptor>> ListResourcesAsync(IMcpContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<McpResourceDescriptor> resources =
        [
            new McpResourceDescriptor
            {
                Uri = CollectionsUri,
                Name = "MongoDB Collections",
                Description = "List of monitored collections with stats, index info, and clean status.",
                MimeType = "application/json",
            },
            new McpResourceDescriptor
            {
                Uri = MonitoringUri,
                Name = "MongoDB Monitoring",
                Description = "Recent calls, call summary (by collection+function), error summary, and slow-call index coverage.",
                MimeType = "application/json",
            },
            new McpResourceDescriptor
            {
                Uri = ClientsUri,
                Name = "MongoDB Monitor Clients",
                Description = "Connected remote monitoring agents and their connection state.",
                MimeType = "application/json",
            },
        ];
        return Task.FromResult(resources);
    }

    public async Task<McpResourceContent> ReadResourceAsync(string uri, IMcpContext context, CancellationToken cancellationToken)
    {
        return uri switch
        {
            CollectionsUri => await BuildCollectionsAsync(cancellationToken),
            MonitoringUri => await BuildMonitoringAsync(cancellationToken),
            ClientsUri => BuildClients(),
            _ => new McpResourceContent { Uri = uri, Text = $"Unknown resource: {uri}" },
        };
    }

    private async Task<McpResourceContent> BuildCollectionsAsync(CancellationToken cancellationToken)
    {
        var items = new List<object>();
        await foreach (var info in _monitor.GetInstancesAsync().WithCancellation(cancellationToken))
        {
            items.Add(new
            {
                configurationName = info.ConfigurationName.Value,
                databaseName = info.DatabaseName,
                collectionName = info.CollectionName,
                server = info.Server,
                registration = info.Registration.ToString(),
                discovery = info.Discovery.ToString(),
                entityTypes = info.EntityTypes,
                isLocal = info.CollectionType != null,
                stats = info.Stats == null ? null : new
                {
                    documentCount = info.Stats.DocumentCount,
                    size = info.Stats.Size,
                    updatedAt = info.Stats.UpdatedAt,
                },
                index = info.Index == null ? null : new
                {
                    current = info.Index.Current?.Length ?? 0,
                    defined = info.Index.Defined?.Length ?? 0,
                    updatedAt = info.Index.UpdatedAt,
                },
                clean = info.Clean == null ? null : new
                {
                    documentsCleaned = info.Clean.DocumentsCleaned,
                    cleanedAt = info.Clean.CleanedAt,
                },
            });
        }

        return new McpResourceContent
        {
            Uri = CollectionsUri,
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(new { collections = items }, JsonOptions),
        };
    }

    private async Task<McpResourceContent> BuildMonitoringAsync(CancellationToken cancellationToken)
    {
        var recentCalls = _monitor.GetCallDtos(CallType.Last).Take(50).ToArray();
        var slowCalls = _monitor.GetCallDtos(CallType.Slow).Take(50).ToArray();
        var callSummary = _monitor.GetCallSummary().ToArray();
        var errorSummary = _monitor.GetErrorSummary().ToArray();

        var slowWithIndex = new List<SlowCallWithIndexInfoDto>();
        await foreach (var item in _monitor.GetSlowCallsWithIndexInfoAsync().WithCancellation(cancellationToken))
        {
            slowWithIndex.Add(item);
            if (slowWithIndex.Count >= 50) break;
        }

        var payload = new
        {
            recentCalls,
            slowCalls,
            callSummary,
            errorSummary,
            slowCallsWithIndex = slowWithIndex,
            connectionPool = _monitor.GetConnectionPoolState(),
        };

        return new McpResourceContent
        {
            Uri = MonitoringUri,
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(payload, JsonOptions),
        };
    }

    private McpResourceContent BuildClients()
    {
        var clients = _monitor.GetMonitorClients().ToArray();
        return new McpResourceContent
        {
            Uri = ClientsUri,
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(new { clients }, JsonOptions),
        };
    }
}

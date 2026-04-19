using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tharga.Mcp;

namespace Tharga.MongoDB.Mcp;

/// <summary>
/// Exposes Tharga.MongoDB actions as MCP tools on the System scope.
/// </summary>
public sealed class MongoDbToolProvider : IMcpToolProvider
{
    private const string TouchToolName = "mongodb.touch";
    private const string RebuildIndexToolName = "mongodb.rebuild_index";

    private static readonly JsonElement CollectionArgSchema = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "configurationName": { "type": "string" },
            "databaseName": { "type": "string" },
            "collectionName": { "type": "string" }
          },
          "required": ["databaseName", "collectionName"]
        }
        """);

    private static readonly JsonElement RebuildIndexArgSchema = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "configurationName": { "type": "string" },
            "databaseName": { "type": "string" },
            "collectionName": { "type": "string" },
            "force": { "type": "boolean" }
          },
          "required": ["databaseName", "collectionName"]
        }
        """);

    private readonly IDatabaseMonitor _monitor;

    public MongoDbToolProvider(IDatabaseMonitor monitor)
    {
        _monitor = monitor;
    }

    public McpScope Scope => McpScope.System;

    public Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(IMcpContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<McpToolDescriptor> tools =
        [
            new McpToolDescriptor
            {
                Name = TouchToolName,
                Description = "Refresh collection stats (document count, size, indexes) from MongoDB.",
                InputSchema = CollectionArgSchema,
            },
            new McpToolDescriptor
            {
                Name = RebuildIndexToolName,
                Description = "Restore or rebuild indexes for a collection according to its defined index model.",
                InputSchema = RebuildIndexArgSchema,
            },
        ];
        return Task.FromResult(tools);
    }

    public async Task<McpToolResult> CallToolAsync(string toolName, JsonElement arguments, IMcpContext context, CancellationToken cancellationToken)
    {
        try
        {
            return toolName switch
            {
                TouchToolName => await TouchAsync(arguments, cancellationToken),
                RebuildIndexToolName => await RebuildIndexAsync(arguments, cancellationToken),
                _ => Error($"Unknown tool: {toolName}"),
            };
        }
        catch (Exception e)
        {
            return Error(e.Message);
        }
    }

    private async Task<McpToolResult> TouchAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var collection = await FindCollectionAsync(arguments, cancellationToken);
        if (collection == null) return Error("Collection not found.");

        await _monitor.TouchAsync(collection);

        var refreshed = await _monitor.GetInstanceAsync(collection);
        return Ok(new
        {
            touched = true,
            collection = collection.CollectionName,
            stats = refreshed?.Stats,
        });
    }

    private async Task<McpToolResult> RebuildIndexAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var collection = await FindCollectionAsync(arguments, cancellationToken);
        if (collection == null) return Error("Collection not found.");

        var force = arguments.TryGetProperty("force", out var f) && f.ValueKind == JsonValueKind.True;
        await _monitor.RestoreIndexAsync(collection, force);

        return Ok(new
        {
            rebuilt = true,
            collection = collection.CollectionName,
            force,
        });
    }

    private async Task<CollectionInfo> FindCollectionAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var configurationName = arguments.TryGetProperty("configurationName", out var c) ? c.GetString() : null;
        var databaseName = arguments.GetProperty("databaseName").GetString();
        var collectionName = arguments.GetProperty("collectionName").GetString();

        return await _monitor.GetInstancesAsync()
            .FirstOrDefaultAsync(
                x => (configurationName == null || x.ConfigurationName.Value == configurationName)
                    && x.DatabaseName == databaseName
                    && x.CollectionName == collectionName,
                cancellationToken);
    }

    private static McpToolResult Ok(object payload)
    {
        return new McpToolResult
        {
            Content = [new McpContent { Type = "text", Text = JsonSerializer.Serialize(payload) }],
        };
    }

    private static McpToolResult Error(string message)
    {
        return new McpToolResult
        {
            IsError = true,
            Content = [new McpContent { Type = "text", Text = message }],
        };
    }
}

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
    private const string RestoreAllIndexesToolName = "mongodb.restore_all_indexes";
    private const string DropIndexToolName = "mongodb.drop_index";
    private const string ResetCacheToolName = "mongodb.reset_cache";
    private const string ClearCallHistoryToolName = "mongodb.clear_call_history";
    private const string FindDuplicatesToolName = "mongodb.find_duplicates";
    private const string ExplainToolName = "mongodb.explain";
    private const string CleanToolName = "mongodb.clean";

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

    private static readonly JsonElement RestoreAllIndexesArgSchema = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "configurationName": { "type": "string" },
            "databaseName": { "type": "string" }
          }
        }
        """);

    private static readonly JsonElement CleanArgSchema = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "configurationName": { "type": "string" },
            "databaseName": { "type": "string" },
            "collectionName": { "type": "string" },
            "cleanGuids": { "type": "boolean" }
          },
          "required": ["databaseName", "collectionName"]
        }
        """);

    private static readonly JsonElement FindDuplicatesArgSchema = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "configurationName": { "type": "string" },
            "databaseName": { "type": "string" },
            "collectionName": { "type": "string" },
            "indexName": { "type": "string" }
          },
          "required": ["databaseName", "collectionName", "indexName"]
        }
        """);

    private static readonly JsonElement ExplainArgSchema = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "callKey": { "type": "string" }
          },
          "required": ["callKey"]
        }
        """);

    private static readonly JsonElement EmptyArgSchema = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {}
        }
        """);

    private static readonly McpToolDescriptor[] AllTools =
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
        new McpToolDescriptor
        {
            Name = RestoreAllIndexesToolName,
            Description = "Iterate every known collection and re-apply its declared indexes. Optional configurationName / databaseName narrow the scope. Returns a summary with success / failure / skipped counts.",
            InputSchema = RestoreAllIndexesArgSchema,
        },
        new McpToolDescriptor
        {
            Name = DropIndexToolName,
            Description = "Drop indexes on a collection that are not declared in code (keeps required _id and lockable indexes). Returns the count before and after.",
            InputSchema = CollectionArgSchema,
        },
        new McpToolDescriptor
        {
            Name = ResetCacheToolName,
            Description = "Reset the in-memory monitor cache (collections, calls, clients).",
            InputSchema = EmptyArgSchema,
        },
        new McpToolDescriptor
        {
            Name = ClearCallHistoryToolName,
            Description = "Clear the recent and slow call history. Does not affect collection or client state.",
            InputSchema = EmptyArgSchema,
        },
        new McpToolDescriptor
        {
            Name = FindDuplicatesToolName,
            Description = "Find documents that block creation of a unique index. Returns sample duplicate-key tuples (DataRead — exposes actual key values).",
            InputSchema = FindDuplicatesArgSchema,
        },
        new McpToolDescriptor
        {
            Name = ExplainToolName,
            Description = "Return the MongoDB explain plan for a previously logged call (DataRead — the plan typically embeds the query filter values).",
            InputSchema = ExplainArgSchema,
        },
        new McpToolDescriptor
        {
            Name = CleanToolName,
            Description = "Remove orphaned/invalid documents from a collection (DataReadWrite — deletes data).",
            InputSchema = CleanArgSchema,
        },
    ];

    private static readonly IReadOnlyDictionary<string, DataAccessLevel> ToolLevels =
        new Dictionary<string, DataAccessLevel>
        {
            [TouchToolName] = DataAccessLevel.Metadata,
            [RebuildIndexToolName] = DataAccessLevel.Metadata,
            [RestoreAllIndexesToolName] = DataAccessLevel.Metadata,
            [DropIndexToolName] = DataAccessLevel.Metadata,
            [ResetCacheToolName] = DataAccessLevel.Metadata,
            [ClearCallHistoryToolName] = DataAccessLevel.Metadata,
            [FindDuplicatesToolName] = DataAccessLevel.DataRead,
            [ExplainToolName] = DataAccessLevel.DataRead,
            [CleanToolName] = DataAccessLevel.DataReadWrite,
        };

    private readonly IDatabaseMonitor _monitor;
    private readonly MongoDbMcpOptions _options;

    public MongoDbToolProvider(IDatabaseMonitor monitor, MongoDbMcpOptions options)
    {
        _monitor = monitor;
        _options = options;
    }

    public McpScope Scope => McpScope.System;

    public Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(IMcpContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<McpToolDescriptor> filtered = AllTools
            .Where(t => ToolLevels[t.Name] <= _options.DataAccess)
            .ToArray();
        return Task.FromResult(filtered);
    }

    public async Task<McpToolResult> CallToolAsync(string toolName, JsonElement arguments, IMcpContext context, CancellationToken cancellationToken)
    {
        if (!ToolLevels.TryGetValue(toolName, out var required))
        {
            return Error($"Unknown tool: {toolName}");
        }
        if (required > _options.DataAccess)
        {
            return Error($"Tool '{toolName}' requires DataAccessLevel.{required} but server is configured for DataAccessLevel.{_options.DataAccess}.");
        }

        try
        {
            return toolName switch
            {
                TouchToolName => await TouchAsync(arguments, cancellationToken),
                RebuildIndexToolName => await RebuildIndexAsync(arguments, cancellationToken),
                RestoreAllIndexesToolName => await RestoreAllIndexesAsync(arguments, cancellationToken),
                DropIndexToolName => await DropIndexAsync(arguments, cancellationToken),
                ResetCacheToolName => await ResetCacheAsync(),
                ClearCallHistoryToolName => ClearCallHistory(),
                FindDuplicatesToolName => await FindDuplicatesAsync(arguments, cancellationToken),
                ExplainToolName => await ExplainAsync(arguments, cancellationToken),
                CleanToolName => await CleanAsync(arguments, cancellationToken),
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

    private async Task<McpToolResult> RestoreAllIndexesAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var configurationName = arguments.TryGetProperty("configurationName", out var c) ? c.GetString() : null;
        var databaseName = arguments.TryGetProperty("databaseName", out var d) ? d.GetString() : null;

        Func<CollectionInfo, bool> filter = null;
        if (configurationName != null || databaseName != null)
        {
            filter = info =>
                (configurationName == null || info.ConfigurationName.Value == configurationName)
                && (databaseName == null || info.DatabaseName == databaseName);
        }

        var summary = await _monitor.RestoreAllIndicesAsync(filter: filter, cancellationToken: cancellationToken);
        return Ok(new
        {
            total = summary.Total,
            succeeded = summary.Succeeded,
            failed = summary.Failed,
            skipped = summary.Skipped,
        });
    }

    private async Task<McpToolResult> DropIndexAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var collection = await FindCollectionAsync(arguments, cancellationToken);
        if (collection == null) return Error("Collection not found.");

        var (before, after) = await _monitor.DropIndexAsync(collection);
        return Ok(new
        {
            collection = collection.CollectionName,
            before,
            after,
            dropped = before - after,
        });
    }

    private async Task<McpToolResult> ResetCacheAsync()
    {
        await _monitor.ResetAsync();
        return Ok(new { reset = true });
    }

    private McpToolResult ClearCallHistory()
    {
        _monitor.ResetCalls();
        return Ok(new { cleared = true });
    }

    private async Task<McpToolResult> FindDuplicatesAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var collection = await FindCollectionAsync(arguments, cancellationToken);
        if (collection == null) return Error("Collection not found.");

        var indexName = arguments.GetProperty("indexName").GetString();
        var blockers = await _monitor.GetIndexBlockersAsync(collection, indexName);
        return Ok(new
        {
            collection = collection.CollectionName,
            indexName,
            duplicates = blockers,
        });
    }

    private async Task<McpToolResult> ExplainAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var raw = arguments.GetProperty("callKey").GetString();
        if (!Guid.TryParse(raw, out var callKey))
        {
            return Error($"Invalid callKey: '{raw}' is not a Guid.");
        }
        var explain = await _monitor.GetExplainAsync(callKey, cancellationToken);
        return Ok(new { callKey, explain });
    }

    private async Task<McpToolResult> CleanAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var collection = await FindCollectionAsync(arguments, cancellationToken);
        if (collection == null) return Error("Collection not found.");

        var cleanGuids = arguments.TryGetProperty("cleanGuids", out var cg) && cg.ValueKind == JsonValueKind.True;
        var info = await _monitor.CleanAsync(collection, cleanGuids);
        return Ok(new
        {
            collection = collection.CollectionName,
            cleanGuids,
            cleaned = info,
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

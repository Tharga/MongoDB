using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tharga.Mcp;

namespace Tharga.MongoDB.Mcp.Atlas;

/// <summary>
/// Exposes a curated read-only slice of the MongoDB Atlas Administration API as MCP tools.
/// All tools are <see cref="DataAccessLevel.Metadata"/>-gated and System scope. Atlas
/// credentials live in <see cref="MongoDbMcpOptions.Atlas"/> — the provider is only registered
/// when that property is non-null (see <see cref="ThargaMcpBuilderExtensions.AddMongoDB"/>).
/// </summary>
internal sealed class AtlasMongoDbToolProvider : IMcpToolProvider
{
    private const string ListClustersToolName = "atlas.list_clusters";
    private const string SuggestedIndexesToolName = "atlas.get_performance_advisor_suggestions";
    private const string OpenAlertsToolName = "atlas.get_open_alerts";

    private static readonly JsonElement EmptyArgSchema = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {}
        }
        """);

    private static readonly JsonElement SuggestedIndexesArgSchema = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "clusterName": { "type": "string", "description": "Name of the Atlas cluster (from atlas.list_clusters)." }
          },
          "required": ["clusterName"]
        }
        """);

    private static readonly McpToolDescriptor[] AllTools =
    [
        new McpToolDescriptor
        {
            Name = ListClustersToolName,
            Description = "List clusters in the configured Atlas project. Returns name, type, state, and MongoDB version per cluster.",
            InputSchema = EmptyArgSchema,
        },
        new McpToolDescriptor
        {
            Name = SuggestedIndexesToolName,
            Description = "Atlas Performance Advisor's suggested indexes for a named cluster. Returns the namespaces, fields, and weights — the same data the Atlas UI surfaces under Performance Advisor → Suggested Indexes.",
            InputSchema = SuggestedIndexesArgSchema,
        },
        new McpToolDescriptor
        {
            Name = OpenAlertsToolName,
            Description = "Currently-firing Atlas alerts in the configured project. Returns alert id, event type, status, and creation time.",
            InputSchema = EmptyArgSchema,
        },
    ];

    private readonly MongoDbMcpOptions _options;
    private readonly AtlasV2HttpClient _client;

    public AtlasMongoDbToolProvider(MongoDbMcpOptions options, AtlasV2HttpClient client)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public McpScope Scope => McpScope.System;

    public Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(IMcpContext context, CancellationToken cancellationToken)
    {
        // All Atlas tools are Metadata level; gate behind the same DataAccess setting as the rest of the package.
        IReadOnlyList<McpToolDescriptor> filtered = _options.DataAccess >= DataAccessLevel.Metadata
            ? AllTools
            : Array.Empty<McpToolDescriptor>();
        return Task.FromResult(filtered);
    }

    public async Task<McpToolResult> CallToolAsync(string toolName, JsonElement arguments, IMcpContext context, CancellationToken cancellationToken)
    {
        if (_options.DataAccess < DataAccessLevel.Metadata)
        {
            return Error($"Tool '{toolName}' requires DataAccessLevel.Metadata but server is configured for DataAccessLevel.{_options.DataAccess}.");
        }

        var groupId = _options.Atlas?.GroupId;
        if (string.IsNullOrEmpty(groupId))
        {
            return Error("Atlas GroupId is not configured. Set MongoDbMcpOptions.Atlas.GroupId to enable Atlas tools.");
        }

        try
        {
            return toolName switch
            {
                ListClustersToolName => await ListClustersAsync(groupId, cancellationToken),
                SuggestedIndexesToolName => await GetSuggestedIndexesAsync(groupId, arguments, cancellationToken),
                OpenAlertsToolName => await GetOpenAlertsAsync(groupId, cancellationToken),
                _ => Error($"Unknown tool: {toolName}"),
            };
        }
        catch (Exception e)
        {
            return Error(e.Message);
        }
    }

    private async Task<McpToolResult> ListClustersAsync(string groupId, CancellationToken cancellationToken)
    {
        var json = await _client.GetJsonAsync($"groups/{Uri.EscapeDataString(groupId)}/clusters", cancellationToken);

        // Surface only the fields callers asked for — keeps the response payload predictable.
        var clusters = json.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
            ? results.EnumerateArray().Select(c => new
            {
                name = c.TryGetProperty("name", out var n) ? n.GetString() : null,
                clusterType = c.TryGetProperty("clusterType", out var t) ? t.GetString() : null,
                stateName = c.TryGetProperty("stateName", out var s) ? s.GetString() : null,
                mongoDBVersion = c.TryGetProperty("mongoDBVersion", out var v) ? v.GetString() : null,
            }).ToArray()
            : Array.Empty<object>();

        return Ok(new { clusters });
    }

    private async Task<McpToolResult> GetSuggestedIndexesAsync(string groupId, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!arguments.TryGetProperty("clusterName", out var clusterEl) || clusterEl.ValueKind != JsonValueKind.String)
        {
            return Error("clusterName is required.");
        }
        var clusterName = clusterEl.GetString();
        if (string.IsNullOrWhiteSpace(clusterName)) return Error("clusterName must not be empty.");

        var path = $"groups/{Uri.EscapeDataString(groupId)}/clusters/{Uri.EscapeDataString(clusterName)}/performanceAdvisor/suggestedIndexes";
        var json = await _client.GetJsonAsync(path, cancellationToken);

        // Forward the relevant fields raw — the schema (namespaces, weight, index keys, query shapes) is well
        // documented, and surfacing the raw JSON keeps Phase 1 forward-compatible if Atlas adds new fields.
        return Ok(new
        {
            cluster = clusterName,
            suggestedIndexes = ExtractArray(json, "suggestedIndexes"),
            shapes = ExtractArray(json, "shapes"),
        });
    }

    private async Task<McpToolResult> GetOpenAlertsAsync(string groupId, CancellationToken cancellationToken)
    {
        var json = await _client.GetJsonAsync($"groups/{Uri.EscapeDataString(groupId)}/alerts?status=OPEN", cancellationToken);

        var alerts = json.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
            ? results.EnumerateArray().Select(a => new
            {
                id = a.TryGetProperty("id", out var i) ? i.GetString() : null,
                eventTypeName = a.TryGetProperty("eventTypeName", out var et) ? et.GetString() : null,
                status = a.TryGetProperty("status", out var s) ? s.GetString() : null,
                created = a.TryGetProperty("created", out var c) ? c.GetString() : null,
                clusterName = a.TryGetProperty("clusterName", out var cn) ? cn.GetString() : null,
                hostnameAndPort = a.TryGetProperty("hostnameAndPort", out var hp) ? hp.GetString() : null,
            }).ToArray()
            : Array.Empty<object>();

        return Ok(new { alerts });
    }

    private static JsonElement ExtractArray(JsonElement root, string property)
    {
        return root.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.Array
            ? el
            : JsonDocument.Parse("[]").RootElement.Clone();
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Tharga.Mcp;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Mcp;
using Tharga.MongoDB.Mcp.Atlas;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class AtlasMongoDbToolProviderTests
{
    private readonly Mock<IMcpContext> _contextMock;

    public AtlasMongoDbToolProviderTests()
    {
        _contextMock = new Mock<IMcpContext>();
        _contextMock.Setup(c => c.Scope).Returns(McpScope.System);
        _contextMock.Setup(c => c.IsDeveloper).Returns(true);
    }

    private static MongoDbApiAccess Access => new()
    {
        PublicKey = "pub-key",
        PrivateKey = "priv-key",
        GroupId = "group-123",
    };

    private static (AtlasMongoDbToolProvider Provider, RecordingHandler Handler) CreateProvider(
        Dictionary<string, string> responseByPath,
        DataAccessLevel level = DataAccessLevel.Metadata,
        MongoDbApiAccess access = null)
    {
        var handler = new RecordingHandler(responseByPath);
        var client = new AtlasV2HttpClient(access ?? Access, handler);
        var options = new MongoDbMcpOptions { DataAccess = level, Atlas = access ?? Access };
        return (new AtlasMongoDbToolProvider(options, client), handler);
    }

    [Fact]
    public void Scope_IsSystem()
    {
        var (provider, _) = CreateProvider(new Dictionary<string, string>());
        provider.Scope.Should().Be(McpScope.System);
    }

    [Fact]
    public async Task ListTools_AtMetadata_ReturnsAllThreeAtlasTools()
    {
        var (provider, _) = CreateProvider(new Dictionary<string, string>());
        var tools = await provider.ListToolsAsync(_contextMock.Object, CancellationToken.None);

        tools.Select(t => t.Name).Should().BeEquivalentTo(
            "atlas.list_clusters",
            "atlas.get_performance_advisor_suggestions",
            "atlas.get_open_alerts");
    }

    [Fact]
    public async Task CallTool_WithoutAtlasAccess_ReturnsError()
    {
        var handler = new RecordingHandler(new Dictionary<string, string>());
        var client = new AtlasV2HttpClient(Access, handler);
        var options = new MongoDbMcpOptions { Atlas = null };
        var provider = new AtlasMongoDbToolProvider(options, client);

        var result = await provider.CallToolAsync("atlas.list_clusters", JsonDocument.Parse("{}").RootElement, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("GroupId is not configured");
    }

    [Fact]
    public async Task ListClusters_HitsExpectedUrl_AndProjectsResults()
    {
        const string body = """
            {
              "results": [
                { "name": "Cluster0", "clusterType": "REPLICASET", "stateName": "IDLE", "mongoDBVersion": "7.0.5", "ignored": "yes" },
                { "name": "Cluster1", "clusterType": "SHARDED",   "stateName": "UPDATING", "mongoDBVersion": "7.0.4" }
              ],
              "totalCount": 2
            }
            """;
        var (provider, handler) = CreateProvider(new Dictionary<string, string>
        {
            ["/api/atlas/v2/groups/group-123/clusters"] = body,
        });

        var result = await provider.CallToolAsync("atlas.list_clusters", JsonDocument.Parse("{}").RootElement, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var payload = JsonDocument.Parse(result.Content[0].Text).RootElement;
        var clusters = payload.GetProperty("clusters").EnumerateArray().ToArray();
        clusters.Should().HaveCount(2);
        clusters[0].GetProperty("name").GetString().Should().Be("Cluster0");
        clusters[0].GetProperty("clusterType").GetString().Should().Be("REPLICASET");
        clusters[0].GetProperty("stateName").GetString().Should().Be("IDLE");
        clusters[0].GetProperty("mongoDBVersion").GetString().Should().Be("7.0.5");
        clusters[0].TryGetProperty("ignored", out _).Should().BeFalse("only the documented fields should leak through");

        handler.Requests.Should().ContainSingle()
            .Which.Headers.Accept.ToString().Should().Contain("vnd.atlas.");
    }

    [Fact]
    public async Task SuggestedIndexes_RequiresClusterName()
    {
        var (provider, _) = CreateProvider(new Dictionary<string, string>());
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await provider.CallToolAsync("atlas.get_performance_advisor_suggestions", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("clusterName is required");
    }

    [Fact]
    public async Task SuggestedIndexes_HitsClusterScopedUrl_AndForwardsRawArrays()
    {
        const string body = """
            {
              "shapes": [{ "namespace": "db.coll", "id": "shape-1", "avgMs": 12.5 }],
              "suggestedIndexes": [
                { "namespace": "db.coll", "weight": 740527872, "index": [{ "Lock": 1 }] }
              ]
            }
            """;
        var (provider, handler) = CreateProvider(new Dictionary<string, string>
        {
            ["/api/atlas/v2/groups/group-123/clusters/Cluster0/performanceAdvisor/suggestedIndexes"] = body,
        });
        var args = JsonDocument.Parse("""{ "clusterName": "Cluster0" }""").RootElement;

        var result = await provider.CallToolAsync("atlas.get_performance_advisor_suggestions", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var payload = JsonDocument.Parse(result.Content[0].Text).RootElement;
        payload.GetProperty("cluster").GetString().Should().Be("Cluster0");
        payload.GetProperty("suggestedIndexes").GetArrayLength().Should().Be(1);
        payload.GetProperty("shapes").GetArrayLength().Should().Be(1);
        payload.GetProperty("suggestedIndexes")[0].GetProperty("weight").GetInt64().Should().Be(740527872L);

        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri.PathAndQuery.Should().Be("/api/atlas/v2/groups/group-123/clusters/Cluster0/performanceAdvisor/suggestedIndexes");
    }

    [Fact]
    public async Task OpenAlerts_HitsAlertsUrl_WithStatusOpenQuery_AndProjectsResults()
    {
        const string body = """
            {
              "results": [
                { "id": "alert-1", "eventTypeName": "OUTSIDE_METRIC_THRESHOLD", "status": "OPEN", "created": "2026-05-09T12:00:00Z", "clusterName": "Cluster0", "hostnameAndPort": "host-1.mongodb.net:27017", "secret": "ignore me" }
              ]
            }
            """;
        var (provider, handler) = CreateProvider(new Dictionary<string, string>
        {
            ["/api/atlas/v2/groups/group-123/alerts?status=OPEN"] = body,
        });

        var result = await provider.CallToolAsync("atlas.get_open_alerts", JsonDocument.Parse("{}").RootElement, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var payload = JsonDocument.Parse(result.Content[0].Text).RootElement;
        var alerts = payload.GetProperty("alerts").EnumerateArray().ToArray();
        alerts.Should().HaveCount(1);
        alerts[0].GetProperty("id").GetString().Should().Be("alert-1");
        alerts[0].GetProperty("eventTypeName").GetString().Should().Be("OUTSIDE_METRIC_THRESHOLD");
        alerts[0].GetProperty("status").GetString().Should().Be("OPEN");
        alerts[0].GetProperty("clusterName").GetString().Should().Be("Cluster0");
        alerts[0].TryGetProperty("secret", out _).Should().BeFalse();

        handler.Requests[0].RequestUri.Query.Should().Contain("status=OPEN");
    }

    [Fact]
    public async Task UnknownTool_ReturnsError()
    {
        var (provider, _) = CreateProvider(new Dictionary<string, string>());

        var result = await provider.CallToolAsync("atlas.bogus", JsonDocument.Parse("{}").RootElement, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("Unknown tool");
    }

    /// <summary>
    /// Test-only handler that matches the request URL's PathAndQuery against a canned response map
    /// and records every outbound request for assertion.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, string> _responses;
        public List<HttpRequestMessage> Requests { get; } = new();

        public RecordingHandler(Dictionary<string, string> responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var key = request.RequestUri.PathAndQuery;
            if (_responses.TryGetValue(key, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"no canned response for {key}"),
            });
        }
    }
}

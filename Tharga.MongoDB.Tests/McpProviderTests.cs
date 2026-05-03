using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Tharga.Mcp;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Mcp;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class McpProviderTests
{
    private readonly Mock<IDatabaseMonitor> _monitorMock;
    private readonly Mock<IMcpContext> _contextMock;

    public McpProviderTests()
    {
        _monitorMock = new Mock<IDatabaseMonitor>();
        _contextMock = new Mock<IMcpContext>();
        _contextMock.Setup(c => c.Scope).Returns(McpScope.System);
        _contextMock.Setup(c => c.IsDeveloper).Returns(true);
    }

    // ---------- Resource provider ----------

    [Fact]
    public void ResourceProvider_Scope_IsSystem()
    {
        var provider = new MongoDbResourceProvider(_monitorMock.Object);
        provider.Scope.Should().Be(McpScope.System);
    }

    [Fact]
    public async Task ResourceProvider_ListResources_ReturnsExpected()
    {
        var provider = new MongoDbResourceProvider(_monitorMock.Object);

        var resources = await provider.ListResourcesAsync(_contextMock.Object, CancellationToken.None);

        resources.Should().HaveCount(3);
        resources.Select(r => r.Uri).Should().Contain([
            "mongodb://collections",
            "mongodb://monitoring",
            "mongodb://clients",
        ]);
    }

    [Fact]
    public async Task ResourceProvider_ReadCollections_ReturnsJson()
    {
        _monitorMock.Setup(m => m.GetInstancesAsync(false, null))
            .Returns(EmptyAsyncEnumerable<CollectionInfo>());

        var provider = new MongoDbResourceProvider(_monitorMock.Object);
        var content = await provider.ReadResourceAsync("mongodb://collections", _contextMock.Object, CancellationToken.None);

        content.Uri.Should().Be("mongodb://collections");
        content.MimeType.Should().Be("application/json");
        var doc = JsonDocument.Parse(content.Text);
        doc.RootElement.GetProperty("collections").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ResourceProvider_ReadMonitoring_ReturnsJson()
    {
        _monitorMock.Setup(m => m.GetCallDtos(It.IsAny<CallType>())).Returns(Array.Empty<CallDto>());
        _monitorMock.Setup(m => m.GetCallSummary()).Returns(Array.Empty<CallSummaryDto>());
        _monitorMock.Setup(m => m.GetErrorSummary()).Returns(Array.Empty<ErrorSummaryDto>());
        _monitorMock.Setup(m => m.GetSlowCallsWithIndexInfoAsync()).Returns(EmptyAsyncEnumerable<SlowCallWithIndexInfoDto>());
        _monitorMock.Setup(m => m.GetConnectionPoolState()).Returns(new ConnectionPoolStateDto
        {
            QueueCount = 0,
            ExecutingCount = 0,
            LastWaitTimeMs = 0,
            RecentMetrics = [],
        });

        var provider = new MongoDbResourceProvider(_monitorMock.Object);
        var content = await provider.ReadResourceAsync("mongodb://monitoring", _contextMock.Object, CancellationToken.None);

        content.Uri.Should().Be("mongodb://monitoring");
        var doc = JsonDocument.Parse(content.Text);
        doc.RootElement.TryGetProperty("recentCalls", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("slowCalls", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("callSummary", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("errorSummary", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("connectionPool", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ResourceProvider_ReadClients_ReturnsJson()
    {
        _monitorMock.Setup(m => m.GetMonitorClients()).Returns(Array.Empty<MonitorClientDto>());

        var provider = new MongoDbResourceProvider(_monitorMock.Object);
        var content = await provider.ReadResourceAsync("mongodb://clients", _contextMock.Object, CancellationToken.None);

        content.Uri.Should().Be("mongodb://clients");
        var doc = JsonDocument.Parse(content.Text);
        doc.RootElement.GetProperty("clients").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ResourceProvider_ReadUnknownUri_ReturnsText()
    {
        var provider = new MongoDbResourceProvider(_monitorMock.Object);
        var content = await provider.ReadResourceAsync("mongodb://does-not-exist", _contextMock.Object, CancellationToken.None);

        content.Text.Should().Contain("Unknown resource");
    }

    // ---------- Tool provider ----------

    [Fact]
    public void ToolProvider_Scope_IsSystem()
    {
        var provider = new MongoDbToolProvider(_monitorMock.Object);
        provider.Scope.Should().Be(McpScope.System);
    }

    [Fact]
    public async Task ToolProvider_ListTools_ReturnsExpected()
    {
        var provider = new MongoDbToolProvider(_monitorMock.Object);
        var tools = await provider.ListToolsAsync(_contextMock.Object, CancellationToken.None);

        tools.Should().HaveCount(3);
        tools.Select(t => t.Name).Should().Contain(["mongodb.touch", "mongodb.rebuild_index", "mongodb.restore_all_indexes"]);
        tools.Should().AllSatisfy(t => t.InputSchema.Should().NotBeNull());
    }

    [Fact]
    public async Task ToolProvider_Touch_CollectionNotFound_ReturnsError()
    {
        _monitorMock.Setup(m => m.GetInstancesAsync(false, null))
            .Returns(EmptyAsyncEnumerable<CollectionInfo>());

        var provider = new MongoDbToolProvider(_monitorMock.Object);
        var args = JsonDocument.Parse("""{"databaseName":"test","collectionName":"missing"}""").RootElement;

        var result = await provider.CallToolAsync("mongodb.touch", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content.Should().HaveCount(1);
        result.Content[0].Text.Should().Contain("not found");
    }

    [Fact]
    public async Task ToolProvider_Touch_CallsMonitorTouchAsync()
    {
        var collection = CreateCollectionInfo("test", "users");
        _monitorMock.Setup(m => m.GetInstancesAsync(false, null))
            .Returns(AsyncEnumerable(collection));
        _monitorMock.Setup(m => m.TouchAsync(It.IsAny<CollectionInfo>()))
            .Returns(Task.CompletedTask);
        _monitorMock.Setup(m => m.GetInstanceAsync(It.IsAny<CollectionFingerprint>()))
            .ReturnsAsync(collection);

        var provider = new MongoDbToolProvider(_monitorMock.Object);
        var args = JsonDocument.Parse("""{"databaseName":"test","collectionName":"users"}""").RootElement;

        var result = await provider.CallToolAsync("mongodb.touch", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _monitorMock.Verify(m => m.TouchAsync(It.Is<CollectionInfo>(c => c.CollectionName == "users")), Times.Once);
    }

    [Fact]
    public async Task ToolProvider_RebuildIndex_CallsRestoreIndexAsync_WithForceFalse()
    {
        var collection = CreateCollectionInfo("test", "users");
        _monitorMock.Setup(m => m.GetInstancesAsync(false, null))
            .Returns(AsyncEnumerable(collection));
        _monitorMock.Setup(m => m.RestoreIndexAsync(It.IsAny<CollectionInfo>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var provider = new MongoDbToolProvider(_monitorMock.Object);
        var args = JsonDocument.Parse("""{"databaseName":"test","collectionName":"users"}""").RootElement;

        var result = await provider.CallToolAsync("mongodb.rebuild_index", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _monitorMock.Verify(m => m.RestoreIndexAsync(It.IsAny<CollectionInfo>(), false), Times.Once);
    }

    [Fact]
    public async Task ToolProvider_RebuildIndex_WithForceTrue_ForwardsFlag()
    {
        var collection = CreateCollectionInfo("test", "users");
        _monitorMock.Setup(m => m.GetInstancesAsync(false, null))
            .Returns(AsyncEnumerable(collection));
        _monitorMock.Setup(m => m.RestoreIndexAsync(It.IsAny<CollectionInfo>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var provider = new MongoDbToolProvider(_monitorMock.Object);
        var args = JsonDocument.Parse("""{"databaseName":"test","collectionName":"users","force":true}""").RootElement;

        await provider.CallToolAsync("mongodb.rebuild_index", args, _contextMock.Object, CancellationToken.None);

        _monitorMock.Verify(m => m.RestoreIndexAsync(It.IsAny<CollectionInfo>(), true), Times.Once);
    }

    [Fact]
    public async Task ToolProvider_UnknownTool_ReturnsError()
    {
        var provider = new MongoDbToolProvider(_monitorMock.Object);
        var args = JsonDocument.Parse("""{}""").RootElement;

        var result = await provider.CallToolAsync("mongodb.nope", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("Unknown tool");
    }

    // ---------- Helpers ----------

    private static CollectionInfo CreateCollectionInfo(string databaseName, string collectionName)
    {
        return new CollectionInfo
        {
            ConfigurationName = "Default",
            DatabaseName = databaseName,
            CollectionName = collectionName,
            Server = "localhost:27017",
            Discovery = Discovery.Database,
            Registration = Registration.Static,
            EntityTypes = [],
            CollectionType = typeof(McpProviderTests),
        };
    }

    private static async IAsyncEnumerable<T> EmptyAsyncEnumerable<T>()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<T> AsyncEnumerable<T>(params T[] items)
    {
        await Task.CompletedTask;
        foreach (var item in items) yield return item;
    }
}

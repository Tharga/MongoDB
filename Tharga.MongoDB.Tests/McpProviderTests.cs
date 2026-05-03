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

    private MongoDbToolProvider CreateToolProvider(DataAccessLevel level = DataAccessLevel.DataReadWrite)
        => new(_monitorMock.Object, new MongoDbMcpOptions { DataAccess = level });

    private MongoDbResourceProvider CreateResourceProvider(DataAccessLevel level = DataAccessLevel.DataReadWrite)
        => new(_monitorMock.Object, new MongoDbMcpOptions { DataAccess = level });

    // ---------- Resource provider ----------

    [Fact]
    public void ResourceProvider_Scope_IsSystem()
    {
        CreateResourceProvider().Scope.Should().Be(McpScope.System);
    }

    [Theory]
    [InlineData(DataAccessLevel.Metadata, new[] { "mongodb://collections", "mongodb://clients" })]
    [InlineData(DataAccessLevel.DataRead, new[] { "mongodb://collections", "mongodb://monitoring", "mongodb://clients" })]
    [InlineData(DataAccessLevel.DataReadWrite, new[] { "mongodb://collections", "mongodb://monitoring", "mongodb://clients" })]
    public async Task ResourceProvider_ListResources_FiltersByAccessLevel(DataAccessLevel level, string[] expectedUris)
    {
        var provider = CreateResourceProvider(level);
        var resources = await provider.ListResourcesAsync(_contextMock.Object, CancellationToken.None);

        resources.Select(r => r.Uri).Should().BeEquivalentTo(expectedUris);
        resources.Should().AllSatisfy(r => r.MimeType.Should().Be("application/json"));
    }

    [Fact]
    public async Task ResourceProvider_ReadMonitoring_AtMetadata_ReturnsLevelMessage()
    {
        var provider = CreateResourceProvider(DataAccessLevel.Metadata);
        var content = await provider.ReadResourceAsync("mongodb://monitoring", _contextMock.Object, CancellationToken.None);

        content.Text.Should().Contain("DataAccessLevel.DataRead");
    }

    [Fact]
    public async Task ResourceProvider_ReadCollections_ReturnsJson()
    {
        _monitorMock.Setup(m => m.GetInstancesAsync(false, null))
            .Returns(EmptyAsyncEnumerable<CollectionInfo>());

        var provider = CreateResourceProvider();
        var content = await provider.ReadResourceAsync("mongodb://collections", _contextMock.Object, CancellationToken.None);

        content.Uri.Should().Be("mongodb://collections");
        content.MimeType.Should().Be("application/json");
        var doc = JsonDocument.Parse(content.Text);
        doc.RootElement.GetProperty("collections").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ResourceProvider_ReadMonitoring_AtDataRead_ReturnsJson()
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

        var provider = CreateResourceProvider(DataAccessLevel.DataRead);
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

        var provider = CreateResourceProvider();
        var content = await provider.ReadResourceAsync("mongodb://clients", _contextMock.Object, CancellationToken.None);

        content.Uri.Should().Be("mongodb://clients");
        var doc = JsonDocument.Parse(content.Text);
        doc.RootElement.GetProperty("clients").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ResourceProvider_ReadUnknownUri_ReturnsText()
    {
        var provider = CreateResourceProvider();
        var content = await provider.ReadResourceAsync("mongodb://does-not-exist", _contextMock.Object, CancellationToken.None);

        content.Text.Should().Contain("Unknown resource");
    }

    // ---------- Tool provider ----------

    [Fact]
    public void ToolProvider_Scope_IsSystem()
    {
        CreateToolProvider().Scope.Should().Be(McpScope.System);
    }

    [Theory]
    [InlineData(DataAccessLevel.Metadata, 6)]
    [InlineData(DataAccessLevel.DataRead, 11)]
    [InlineData(DataAccessLevel.DataReadWrite, 12)]
    public async Task ToolProvider_ListTools_FiltersByAccessLevel(DataAccessLevel level, int expectedCount)
    {
        var provider = CreateToolProvider(level);
        var tools = await provider.ListToolsAsync(_contextMock.Object, CancellationToken.None);

        tools.Should().HaveCount(expectedCount);
        tools.Should().AllSatisfy(t => t.InputSchema.Should().NotBeNull());
    }

    [Fact]
    public async Task ToolProvider_ListTools_AtMetadata_OmitsDataReadAndWriteTools()
    {
        var provider = CreateToolProvider(DataAccessLevel.Metadata);
        var tools = await provider.ListToolsAsync(_contextMock.Object, CancellationToken.None);

        tools.Select(t => t.Name).Should().NotContain([
            "mongodb.find_duplicates",
            "mongodb.explain",
            "mongodb.clean",
            "mongodb.get_document",
            "mongodb.list_documents",
            "mongodb.compare_schema",
        ]);
    }

    [Fact]
    public async Task ToolProvider_CallFindDuplicates_AtMetadata_ReturnsLevelError()
    {
        var provider = CreateToolProvider(DataAccessLevel.Metadata);
        var args = JsonDocument.Parse("""{"databaseName":"test","collectionName":"users","indexName":"name_1"}""").RootElement;

        var result = await provider.CallToolAsync("mongodb.find_duplicates", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("DataAccessLevel.DataRead");
    }

    [Fact]
    public async Task ToolProvider_CallClean_AtDataRead_ReturnsLevelError()
    {
        var provider = CreateToolProvider(DataAccessLevel.DataRead);
        var args = JsonDocument.Parse("""{"databaseName":"test","collectionName":"users"}""").RootElement;

        var result = await provider.CallToolAsync("mongodb.clean", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("DataAccessLevel.DataReadWrite");
    }

    [Fact]
    public async Task ToolProvider_Touch_CollectionNotFound_ReturnsError()
    {
        _monitorMock.Setup(m => m.GetInstancesAsync(false, null))
            .Returns(EmptyAsyncEnumerable<CollectionInfo>());

        var provider = CreateToolProvider();
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

        var provider = CreateToolProvider();
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

        var provider = CreateToolProvider();
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

        var provider = CreateToolProvider();
        var args = JsonDocument.Parse("""{"databaseName":"test","collectionName":"users","force":true}""").RootElement;

        await provider.CallToolAsync("mongodb.rebuild_index", args, _contextMock.Object, CancellationToken.None);

        _monitorMock.Verify(m => m.RestoreIndexAsync(It.IsAny<CollectionInfo>(), true), Times.Once);
    }

    [Fact]
    public async Task ToolProvider_UnknownTool_ReturnsError()
    {
        var provider = CreateToolProvider();
        var args = JsonDocument.Parse("""{}""").RootElement;

        var result = await provider.CallToolAsync("mongodb.nope", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("Unknown tool");
    }

    // ---------- New tools (parity feature) ----------

    [Fact]
    public async Task ToolProvider_DropIndex_CallsDropIndexAsync()
    {
        var collection = CreateCollectionInfo("test", "users");
        _monitorMock.Setup(m => m.GetInstancesAsync(false, null))
            .Returns(AsyncEnumerable(collection));
        _monitorMock.Setup(m => m.DropIndexAsync(It.IsAny<CollectionInfo>()))
            .ReturnsAsync((Before: 5, After: 2));

        var provider = CreateToolProvider();
        var args = JsonDocument.Parse("""{"databaseName":"test","collectionName":"users"}""").RootElement;

        var result = await provider.CallToolAsync("mongodb.drop_index", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _monitorMock.Verify(m => m.DropIndexAsync(It.IsAny<CollectionInfo>()), Times.Once);
        var doc = JsonDocument.Parse(result.Content[0].Text);
        doc.RootElement.GetProperty("before").GetInt32().Should().Be(5);
        doc.RootElement.GetProperty("after").GetInt32().Should().Be(2);
        doc.RootElement.GetProperty("dropped").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task ToolProvider_ResetCache_CallsResetAsync()
    {
        _monitorMock.Setup(m => m.ResetAsync()).Returns(Task.CompletedTask);

        var provider = CreateToolProvider();
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await provider.CallToolAsync("mongodb.reset_cache", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _monitorMock.Verify(m => m.ResetAsync(), Times.Once);
    }

    [Fact]
    public async Task ToolProvider_ClearCallHistory_CallsResetCalls()
    {
        var provider = CreateToolProvider();
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await provider.CallToolAsync("mongodb.clear_call_history", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _monitorMock.Verify(m => m.ResetCalls(), Times.Once);
    }

    [Fact]
    public async Task ToolProvider_FindDuplicates_CallsGetIndexBlockersAsync()
    {
        var collection = CreateCollectionInfo("test", "users");
        _monitorMock.Setup(m => m.GetInstancesAsync(false, null))
            .Returns(AsyncEnumerable(collection));
        _monitorMock.Setup(m => m.GetIndexBlockersAsync(It.IsAny<CollectionInfo>(), "name_1"))
            .ReturnsAsync(new List<string[]> { new[] { "Alice" }, new[] { "Bob" } });

        var provider = CreateToolProvider();
        var args = JsonDocument.Parse("""{"databaseName":"test","collectionName":"users","indexName":"name_1"}""").RootElement;

        var result = await provider.CallToolAsync("mongodb.find_duplicates", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _monitorMock.Verify(m => m.GetIndexBlockersAsync(It.IsAny<CollectionInfo>(), "name_1"), Times.Once);
    }

    [Fact]
    public async Task ToolProvider_Explain_CallsGetExplainAsync()
    {
        var key = Guid.NewGuid();
        _monitorMock.Setup(m => m.GetExplainAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"plan\":\"COLLSCAN\"}");

        var provider = CreateToolProvider();
        var args = JsonDocument.Parse($$$"""{"callKey":"{{{key}}}"}""").RootElement;

        var result = await provider.CallToolAsync("mongodb.explain", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _monitorMock.Verify(m => m.GetExplainAsync(key, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ToolProvider_Explain_InvalidGuid_ReturnsError()
    {
        var provider = CreateToolProvider();
        var args = JsonDocument.Parse("""{"callKey":"not-a-guid"}""").RootElement;

        var result = await provider.CallToolAsync("mongodb.explain", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("Invalid callKey");
    }

    [Fact]
    public async Task ToolProvider_Clean_CallsCleanAsync()
    {
        var collection = CreateCollectionInfo("test", "users");
        _monitorMock.Setup(m => m.GetInstancesAsync(false, null))
            .Returns(AsyncEnumerable(collection));
        _monitorMock.Setup(m => m.CleanAsync(It.IsAny<CollectionInfo>(), false))
            .ReturnsAsync(new CleanInfo
            {
                SchemaFingerprint = "x",
                CleanedAt = DateTime.UtcNow,
                DocumentsCleaned = 7,
            });

        var provider = CreateToolProvider();
        var args = JsonDocument.Parse("""{"databaseName":"test","collectionName":"users"}""").RootElement;

        var result = await provider.CallToolAsync("mongodb.clean", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _monitorMock.Verify(m => m.CleanAsync(It.IsAny<CollectionInfo>(), false), Times.Once);
    }

    [Fact]
    public async Task ToolProvider_Clean_WithCleanGuids_ForwardsFlag()
    {
        var collection = CreateCollectionInfo("test", "users");
        _monitorMock.Setup(m => m.GetInstancesAsync(false, null))
            .Returns(AsyncEnumerable(collection));
        _monitorMock.Setup(m => m.CleanAsync(It.IsAny<CollectionInfo>(), true))
            .ReturnsAsync(new CleanInfo
            {
                SchemaFingerprint = "x",
                CleanedAt = DateTime.UtcNow,
                DocumentsCleaned = 0,
            });

        var provider = CreateToolProvider();
        var args = JsonDocument.Parse("""{"databaseName":"test","collectionName":"users","cleanGuids":true}""").RootElement;

        await provider.CallToolAsync("mongodb.clean", args, _contextMock.Object, CancellationToken.None);

        _monitorMock.Verify(m => m.CleanAsync(It.IsAny<CollectionInfo>(), true), Times.Once);
    }

    // ---------- Document inspection (new in this feature) ----------

    [Fact]
    public async Task ToolProvider_GetDocument_AtMetadata_ReturnsLevelError()
    {
        var provider = CreateToolProvider(DataAccessLevel.Metadata);
        var args = JsonDocument.Parse("""{"databaseName":"test","collectionName":"users","id":"x"}""").RootElement;

        var result = await provider.CallToolAsync("mongodb.get_document", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("DataAccessLevel.DataRead");
    }

    [Theory]
    [InlineData("11111111-2222-3333-4444-555555555555")] // Guid
    [InlineData("507f1f77bcf86cd799439011")]             // ObjectId
    [InlineData("user-key-123")]                         // string
    public async Task ToolProvider_GetDocument_HappyPath_DispatchesToMonitor(string idRaw)
    {
        var collection = CreateCollectionInfo("test", "users");
        _monitorMock.Setup(m => m.GetInstancesAsync(false, null))
            .Returns(AsyncEnumerable(collection));
        _monitorMock.Setup(m => m.GetDocumentAsync(It.IsAny<CollectionInfo>(), idRaw, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentDto { Id = idRaw, Json = "{\"_id\":\"" + idRaw + "\"}" });

        var provider = CreateToolProvider();
        var args = JsonDocument.Parse($$$"""{"databaseName":"test","collectionName":"users","id":"{{{idRaw}}}"}""").RootElement;

        var result = await provider.CallToolAsync("mongodb.get_document", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _monitorMock.Verify(m => m.GetDocumentAsync(It.IsAny<CollectionInfo>(), idRaw, It.IsAny<CancellationToken>()), Times.Once);
        var doc = JsonDocument.Parse(result.Content[0].Text);
        doc.RootElement.GetProperty("id").GetString().Should().Be(idRaw);
    }

    [Fact]
    public async Task ToolProvider_GetDocument_Missing_ReturnsError()
    {
        var collection = CreateCollectionInfo("test", "users");
        _monitorMock.Setup(m => m.GetInstancesAsync(false, null))
            .Returns(AsyncEnumerable(collection));
        _monitorMock.Setup(m => m.GetDocumentAsync(It.IsAny<CollectionInfo>(), "missing-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentDto)null);

        var provider = CreateToolProvider();
        var args = JsonDocument.Parse("""{"databaseName":"test","collectionName":"users","id":"missing-id"}""").RootElement;

        var result = await provider.CallToolAsync("mongodb.get_document", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("No document found");
    }

    [Fact]
    public async Task ToolProvider_ListDocuments_DefaultLimit_DispatchesToMonitor()
    {
        var collection = CreateCollectionInfo("test", "users");
        _monitorMock.Setup(m => m.GetInstancesAsync(false, null))
            .Returns(AsyncEnumerable(collection));
        _monitorMock.Setup(m => m.ListDocumentsAsync(
                It.IsAny<CollectionInfo>(),
                It.Is<DocumentListQuery>(q => q.Limit == 20 && q.Skip == 0 && q.FilterJson == null && q.SortJson == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentListDto
            {
                Documents = [new DocumentDto { Id = "1", Json = "{}" }],
                TotalReturned = 1,
                Truncated = false,
            });

        var provider = CreateToolProvider();
        var args = JsonDocument.Parse("""{"databaseName":"test","collectionName":"users"}""").RootElement;

        var result = await provider.CallToolAsync("mongodb.list_documents", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var doc = JsonDocument.Parse(result.Content[0].Text);
        doc.RootElement.GetProperty("totalReturned").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("truncated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ToolProvider_ListDocuments_WithFilterAndSort_ForwardsArgs()
    {
        var collection = CreateCollectionInfo("test", "users");
        _monitorMock.Setup(m => m.GetInstancesAsync(false, null))
            .Returns(AsyncEnumerable(collection));
        _monitorMock.Setup(m => m.ListDocumentsAsync(It.IsAny<CollectionInfo>(), It.IsAny<DocumentListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentListDto
            {
                Documents = Array.Empty<DocumentDto>(),
                TotalReturned = 0,
                Truncated = false,
            });

        var provider = CreateToolProvider();
        var args = JsonDocument.Parse("""{"databaseName":"test","collectionName":"users","limit":50,"skip":10,"filter":"{\"Status\":\"Active\"}","sort":"{\"CreatedAt\":-1}"}""").RootElement;

        await provider.CallToolAsync("mongodb.list_documents", args, _contextMock.Object, CancellationToken.None);

        _monitorMock.Verify(m => m.ListDocumentsAsync(
            It.IsAny<CollectionInfo>(),
            It.Is<DocumentListQuery>(q =>
                q.Limit == 50 && q.Skip == 10 &&
                q.FilterJson == "{\"Status\":\"Active\"}" &&
                q.SortJson == "{\"CreatedAt\":-1}"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ToolProvider_ListDocuments_InvalidFilter_PropagatesAsError()
    {
        var collection = CreateCollectionInfo("test", "users");
        _monitorMock.Setup(m => m.GetInstancesAsync(false, null))
            .Returns(AsyncEnumerable(collection));
        _monitorMock.Setup(m => m.ListDocumentsAsync(It.IsAny<CollectionInfo>(), It.IsAny<DocumentListQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FormatException("Invalid filter JSON: nope"));

        var provider = CreateToolProvider();
        var args = JsonDocument.Parse("""{"databaseName":"test","collectionName":"users","filter":"not-json"}""").RootElement;

        var result = await provider.CallToolAsync("mongodb.list_documents", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("Invalid filter JSON");
    }

    [Fact]
    public async Task ToolProvider_CompareSchema_DispatchesToMonitor_AndReturnsFields()
    {
        var collection = CreateCollectionInfo("test", "users");
        _monitorMock.Setup(m => m.GetInstancesAsync(false, null))
            .Returns(AsyncEnumerable(collection));
        _monitorMock.Setup(m => m.CompareSchemaAsync(It.IsAny<CollectionInfo>(), 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemaComparisonDto
            {
                SampleSize = 50,
                SampledCount = 25,
                EntityTypes = ["UserEntity"],
                Fields =
                [
                    new SchemaComparisonField { Name = "Id", Classification = SchemaFieldClassification.Full, CoverageCount = 25, DeclaredOnEntity = true },
                    new SchemaComparisonField { Name = "EMail", Classification = SchemaFieldClassification.Partial, CoverageCount = 12, DeclaredOnEntity = true },
                    new SchemaComparisonField { Name = "MissingFromAll", Classification = SchemaFieldClassification.EntityOnly, CoverageCount = 0, DeclaredOnEntity = true },
                    new SchemaComparisonField { Name = "ExtraField", Classification = SchemaFieldClassification.DocumentOnly, CoverageCount = 5, DeclaredOnEntity = false },
                ],
            });

        var provider = CreateToolProvider();
        var args = JsonDocument.Parse("""{"databaseName":"test","collectionName":"users"}""").RootElement;

        var result = await provider.CallToolAsync("mongodb.compare_schema", args, _contextMock.Object, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var doc = JsonDocument.Parse(result.Content[0].Text);
        doc.RootElement.GetProperty("sampledCount").GetInt32().Should().Be(25);
        doc.RootElement.GetProperty("fields").GetArrayLength().Should().Be(4);
    }

    [Fact]
    public async Task ToolProvider_CompareSchema_DefaultsSampleSizeTo50()
    {
        var collection = CreateCollectionInfo("test", "users");
        _monitorMock.Setup(m => m.GetInstancesAsync(false, null))
            .Returns(AsyncEnumerable(collection));
        _monitorMock.Setup(m => m.CompareSchemaAsync(It.IsAny<CollectionInfo>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemaComparisonDto
            {
                SampleSize = 50,
                SampledCount = 0,
                EntityTypes = [],
                Fields = [],
            });

        var provider = CreateToolProvider();
        var args = JsonDocument.Parse("""{"databaseName":"test","collectionName":"users"}""").RootElement;

        await provider.CallToolAsync("mongodb.compare_schema", args, _contextMock.Object, CancellationToken.None);

        _monitorMock.Verify(m => m.CompareSchemaAsync(It.IsAny<CollectionInfo>(), 50, It.IsAny<CancellationToken>()), Times.Once);
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

using System;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class RemoteActionDelegationTests
{
    private readonly DatabaseMonitor _monitor;
    private readonly Mock<IRemoteActionDispatcher> _dispatcherMock;
    private readonly Mock<ICollectionCache> _cacheMock;

    public RemoteActionDelegationTests()
    {
        var factoryMock = new Mock<IMongoDbServiceFactory>();
        factoryMock.Setup(f => f.SourceName).Returns("TestServer/TestApp");

        var instanceMock = new Mock<IMongoDbInstance>();
        instanceMock.Setup(i => i.RegisteredCollections).Returns(new ConcurrentDictionary<Type, Type>());

        _dispatcherMock = new Mock<IRemoteActionDispatcher>();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IRemoteActionDispatcher>(_dispatcherMock.Object);
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var repositoryConfigMock = new Mock<IRepositoryConfiguration>();
        var collectionProviderMock = new Mock<ICollectionProvider>();
        var callLibrary = new CallLibrary(Options.Create(new DatabaseOptions { Monitor = new MonitorOptions() }));
        _cacheMock = new Mock<ICollectionCache>();
        _cacheMock.Setup(c => c.LoadAsync()).Returns(Task.CompletedTask);
        _cacheMock.Setup(c => c.GetKeys()).Returns(Array.Empty<string>());
        _cacheMock.Setup(c => c.GetAll()).Returns(Array.Empty<CollectionInfo>());

        var queueMonitorMock = new Mock<IQueueMonitor>();

        var options = Options.Create(new DatabaseOptions
        {
            Monitor = new MonitorOptions(),
        });

        _monitor = new DatabaseMonitor(
            factoryMock.Object,
            instanceMock.Object,
            serviceProvider,
            repositoryConfigMock.Object,
            collectionProviderMock.Object,
            callLibrary,
            _cacheMock.Object,
            queueMonitorMock.Object,
            options,
            NullLogger<DatabaseMonitor>.Instance);

        _monitor.Start(serviceProvider);
    }

    private CollectionInfo CreateRemoteCollection(string configName = "Default", string dbName = "TestDb", string collName = "TestCol")
    {
        return new CollectionInfo
        {
            ConfigurationName = configName,
            DatabaseName = dbName,
            CollectionName = collName,
            Server = "remote-server:27017",
            Discovery = Discovery.Database,
            Registration = Registration.Static,
            EntityTypes = ["TestEntity"],
            CollectionType = null, // Remote — no local type
        };
    }

    private void IngestRemoteCollectionWithAgent(CollectionInfo collection, string sourceName, string connectionId)
    {
        // Simulate a remote agent sending collection info
        _monitor.IngestCollectionInfo(new RemoteCollectionInfoDto
        {
            ConfigurationName = collection.ConfigurationName.Value,
            DatabaseName = collection.DatabaseName,
            CollectionName = collection.CollectionName,
            SourceName = sourceName,
            Server = collection.Server,
            Discovery = collection.Discovery.ToString(),
            Registration = collection.Registration.ToString(),
            EntityTypes = collection.EntityTypes,
        }, connectionId);

        // Simulate the agent connecting
        _monitor.IngestClientConnected(new MonitorClientDto
        {
            Instance = Guid.NewGuid(),
            ConnectionId = connectionId,
            Machine = "Agent-Machine",
            Type = "TestAgent",
            Version = "1.0",
            IsConnected = true,
            ConnectTime = DateTime.UtcNow,
            SourceName = sourceName,
        });
    }

    [Fact]
    public void FindConnectionIdBySource_ReturnsNull_WhenNoAgentConnected()
    {
        var result = _monitor.FindConnectionIdBySource("UnknownSource");

        result.Should().BeNull();
    }

    [Fact]
    public void FindConnectionIdBySource_ReturnsConnectionId_WhenAgentConnected()
    {
        var collection = CreateRemoteCollection();
        IngestRemoteCollectionWithAgent(collection, "Agent-1/OrderService", "conn-123");

        var result = _monitor.FindConnectionIdBySource("Agent-1/OrderService");

        result.Should().Be("conn-123");
    }

    [Fact]
    public void FindConnectionIdBySource_ReturnsNull_WhenAgentDisconnected()
    {
        var collection = CreateRemoteCollection();
        IngestRemoteCollectionWithAgent(collection, "Agent-1/OrderService", "conn-123");

        _monitor.IngestClientDisconnected("conn-123");

        var result = _monitor.FindConnectionIdBySource("Agent-1/OrderService");

        result.Should().BeNull();
    }

    [Fact]
    public void GetCollectionSources_ReturnsSource_AfterIngest()
    {
        var collection = CreateRemoteCollection();
        IngestRemoteCollectionWithAgent(collection, "Agent-1/OrderService", "conn-123");

        var sources = _monitor.GetCollectionSources(collection.Key);

        sources.Should().Contain("Agent-1/OrderService");
    }

    [Fact]
    public async Task GetInstanceAsync_ReturnsRemoteCollection()
    {
        var collection = CreateRemoteCollection();
        IngestRemoteCollectionWithAgent(collection, "Agent-1/OrderService", "conn-123");

        var fingerprint = new CollectionFingerprint
        {
            ConfigurationName = collection.ConfigurationName,
            DatabaseName = collection.DatabaseName,
            CollectionName = collection.CollectionName,
        };

        var result = await _monitor.GetInstanceAsync(fingerprint);

        result.Should().NotBeNull();
        result.CollectionType.Should().BeNull();
        result.Server.Should().Be("remote-server:27017");
    }

    [Fact]
    public async Task TouchAsync_DelegatesToRemoteDispatcher_ForRemoteCollection()
    {
        var collection = CreateRemoteCollection();
        IngestRemoteCollectionWithAgent(collection, "Agent-1/OrderService", "conn-123");

        _dispatcherMock
            .Setup(d => d.TouchAsync("conn-123", It.IsAny<CollectionInfo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _monitor.TouchAsync(collection);

        _dispatcherMock.Verify(
            d => d.TouchAsync("conn-123", It.Is<CollectionInfo>(c => c.CollectionName == "TestCol"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DropIndexAsync_DelegatesToRemoteDispatcher_ForRemoteCollection()
    {
        var collection = CreateRemoteCollection();
        IngestRemoteCollectionWithAgent(collection, "Agent-1/OrderService", "conn-123");

        _dispatcherMock
            .Setup(d => d.DropIndexAsync("conn-123", It.IsAny<CollectionInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((5, 2));

        var result = await _monitor.DropIndexAsync(collection);

        result.Before.Should().Be(5);
        result.After.Should().Be(2);
        _dispatcherMock.Verify(
            d => d.DropIndexAsync("conn-123", It.IsAny<CollectionInfo>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RestoreIndexAsync_DelegatesToRemoteDispatcher_ForRemoteCollection()
    {
        var collection = CreateRemoteCollection();
        IngestRemoteCollectionWithAgent(collection, "Agent-1/OrderService", "conn-123");

        _dispatcherMock
            .Setup(d => d.RestoreIndexAsync("conn-123", It.IsAny<CollectionInfo>(), true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _monitor.RestoreIndexAsync(collection, true);

        _dispatcherMock.Verify(
            d => d.RestoreIndexAsync("conn-123", It.IsAny<CollectionInfo>(), true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CleanAsync_DelegatesToRemoteDispatcher_ForRemoteCollection()
    {
        var collection = CreateRemoteCollection();
        IngestRemoteCollectionWithAgent(collection, "Agent-1/OrderService", "conn-123");

        var expectedCleanInfo = new CleanInfo { DocumentsCleaned = 42, CleanedAt = DateTime.UtcNow, SchemaFingerprint = "test" };
        _dispatcherMock
            .Setup(d => d.CleanAsync("conn-123", It.IsAny<CollectionInfo>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedCleanInfo);

        var result = await _monitor.CleanAsync(collection, true);

        result.DocumentsCleaned.Should().Be(42);
        _dispatcherMock.Verify(
            d => d.CleanAsync("conn-123", It.IsAny<CollectionInfo>(), true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetIndexBlockersAsync_DelegatesToRemoteDispatcher_ForRemoteCollection()
    {
        var collection = CreateRemoteCollection();
        IngestRemoteCollectionWithAgent(collection, "Agent-1/OrderService", "conn-123");

        var blockers = new[] { new[] { "id1", "id2" } };
        _dispatcherMock
            .Setup(d => d.GetIndexBlockersAsync("conn-123", It.IsAny<CollectionInfo>(), "Value", It.IsAny<CancellationToken>()))
            .ReturnsAsync(blockers);

        var result = await _monitor.GetIndexBlockersAsync(collection, "Value");

        result.Should().HaveCount(1);
        _dispatcherMock.Verify(
            d => d.GetIndexBlockersAsync("conn-123", It.IsAny<CollectionInfo>(), "Value", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TouchAsync_ThrowsWhenNoAgentConnected_ForRemoteCollection()
    {
        var collection = CreateRemoteCollection();
        // Don't ingest any agent — no source mapping

        var act = () => _monitor.TouchAsync(collection);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no connected agent*");
    }

    [Fact]
    public async Task RefreshStatsAsync_SkipsLocalDbAccess_ForRemoteCollection()
    {
        var collection = CreateRemoteCollection();
        IngestRemoteCollectionWithAgent(collection, "Agent-1/OrderService", "conn-123");

        // Should not throw — skips local DB access for remote collections
        await _monitor.RefreshStatsAsync(new CollectionFingerprint
        {
            ConfigurationName = collection.ConfigurationName,
            DatabaseName = collection.DatabaseName,
            CollectionName = collection.CollectionName,
        });
    }
}

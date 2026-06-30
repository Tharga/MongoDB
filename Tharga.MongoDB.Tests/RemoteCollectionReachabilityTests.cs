using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Covers reachability-aware action gating and disconnect cleanup:
/// a remote collection is only actionable while some source (a connected agent, or this server
/// directly) can service it, and a disconnected agent's collections stop being offered. Regression
/// guard for the "remote-only but no connected agent was found" error appearing on enabled buttons.
/// </summary>
public class RemoteCollectionReachabilityTests
{
    private readonly DatabaseMonitor _monitor;
    private readonly Mock<IRemoteActionDispatcher> _dispatcherMock;

    public RemoteCollectionReachabilityTests()
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

        // A real in-memory cache so agent reports persist and reads flow back through it, matching the
        // production path where remote-reported collections live in the _monitor-backed cache.
        var cache = new MemoryCollectionCache();

        var queueMonitorMock = new Mock<IQueueMonitor>();
        var options = Options.Create(new DatabaseOptions { Monitor = new MonitorOptions() });

        _monitor = new DatabaseMonitor(
            factoryMock.Object,
            instanceMock.Object,
            serviceProvider,
            repositoryConfigMock.Object,
            collectionProviderMock.Object,
            callLibrary,
            cache,
            queueMonitorMock.Object,
            new ConnectionPoolMonitor(),
            options,
            NullLogger<DatabaseMonitor>.Instance);

        _monitor.Start(serviceProvider);
    }

    private static CollectionInfo RemoteCollection(Registration registration = Registration.Static, string collName = "TestCol")
    {
        return new CollectionInfo
        {
            ConfigurationName = "Default",
            DatabaseName = "TestDb",
            CollectionName = collName,
            Server = "remote-server:27017",
            Discovery = Discovery.Database,
            Registration = registration,
            EntityTypes = ["TestEntity"],
            CollectionType = null, // remote — no local type
        };
    }

    private void IngestWithAgent(CollectionInfo collection, string sourceName, string connectionId)
    {
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
    public void CanExecuteActions_True_ForRemoteCollectionWithConnectedAgent()
    {
        var collection = RemoteCollection();
        IngestWithAgent(collection, "Agent-1/Svc", "conn-1");

        _monitor.CanExecuteActions(collection).Should().BeTrue();
    }

    [Fact]
    public void CanExecuteActions_False_ForNotInCodeCollection_EvenWithConnectedAgent()
    {
        // NotInCode can't be serviced anywhere — neither this server nor the reporting agent has
        // code to run against it, so no action should be offered.
        var collection = RemoteCollection(Registration.NotInCode);
        IngestWithAgent(collection, "Agent-1/Svc", "conn-1");

        _monitor.CanExecuteActions(collection).Should().BeFalse();
    }

    [Fact]
    public async Task TouchAsync_RejectsNotInCode_WithRegistrationMessage()
    {
        var collection = RemoteCollection(Registration.NotInCode);
        IngestWithAgent(collection, "Agent-1/Svc", "conn-1");

        var act = () => _monitor.TouchAsync(collection);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not support*");
    }

    [Fact]
    public void CanExecuteActions_False_AfterReportingAgentDisconnects()
    {
        var collection = RemoteCollection();
        IngestWithAgent(collection, "Agent-1/Svc", "conn-1");

        _monitor.IngestClientDisconnected("conn-1");

        _monitor.CanExecuteActions(collection).Should().BeFalse(
            "the only agent that could service the action is gone");
    }

    [Fact]
    public async Task IngestClientDisconnected_DropsReachability_ButKeepsPersistedData_WhenLastSourceGone()
    {
        var collection = RemoteCollection();
        IngestWithAgent(collection, "Agent-1/Svc", "conn-1");
        _monitor.GetCollectionSources(collection.Key).Should().Contain("Agent-1/Svc");

        _monitor.IngestClientDisconnected("conn-1");

        _monitor.GetCollectionSources(collection.Key).Should().BeEmpty(
            "the gone agent no longer counts as a live source");
        _monitor.CanExecuteActions(collection).Should().BeFalse(
            "no live source can service an action");

        // The data itself survives in the cache with its reported age — it is no longer a ghost-removal.
        var persisted = await _monitor.GetInstanceAsync(collection);
        persisted.Should().NotBeNull("the report is persisted for later use, not evicted on disconnect");
        persisted.ReportedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task IngestClientDisconnected_KeepsCollection_WhenAnotherSourceRemains()
    {
        var collection = RemoteCollection();
        IngestWithAgent(collection, "Agent-1/Svc", "conn-1");
        IngestWithAgent(collection, "Agent-2/Svc", "conn-2");

        _monitor.IngestClientDisconnected("conn-1");

        _monitor.GetCollectionSources(collection.Key).Should().BeEquivalentTo(["Agent-2/Svc"],
            "the surviving agent still reports the collection");
        _monitor.CanExecuteActions(collection).Should().BeTrue();

        // And the action dispatches to the surviving agent's connection.
        _dispatcherMock
            .Setup(d => d.TouchAsync("conn-2", It.IsAny<CollectionInfo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _monitor.TouchAsync(collection);

        _dispatcherMock.Verify(
            d => d.TouchAsync("conn-2", It.IsAny<CollectionInfo>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TouchAsync_Throws_AfterReportingAgentDisconnects()
    {
        // The exact reported scenario: a remote collection whose agent has gone away must not
        // silently look actionable — the action throws a clear error rather than a stale dispatch.
        var collection = RemoteCollection();
        IngestWithAgent(collection, "Agent-1/Svc", "conn-1");
        _monitor.IngestClientDisconnected("conn-1");

        var act = () => _monitor.TouchAsync(collection);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no connected agent*");
    }

    [Fact]
    public async Task IngestCollectionDropped_RemovesPersistedRecord_AndRaisesDroppedEvent()
    {
        var collection = RemoteCollection();
        IngestWithAgent(collection, "Agent-1/Svc", "conn-1");

        CollectionDroppedEventArgs dropped = null;
        _monitor.CollectionDroppedEvent += (_, e) => dropped = e;

        _monitor.IngestCollectionDropped("Agent-1/Svc", collection.ConfigurationName.Value, collection.DatabaseName, collection.CollectionName);

        _monitor.GetCollectionSources(collection.Key).Should().BeEmpty();
        // Unlike a disconnect, a genuine drop removes the persisted record entirely.
        (await _monitor.GetInstanceAsync(collection)).Should().BeNull();
        dropped.Should().NotBeNull();
        dropped.CollectionName.Should().Be(collection.CollectionName);
        dropped.DatabaseName.Should().Be(collection.DatabaseName);
    }

    [Fact]
    public void IngestCollectionDropped_KeepsCollection_WhenAnotherSourceStillReportsIt()
    {
        var collection = RemoteCollection();
        IngestWithAgent(collection, "Agent-1/Svc", "conn-1");
        IngestWithAgent(collection, "Agent-2/Svc", "conn-2");

        _monitor.IngestCollectionDropped("Agent-1/Svc", collection.ConfigurationName.Value, collection.DatabaseName, collection.CollectionName);

        _monitor.GetCollectionSources(collection.Key).Should().BeEquivalentTo(["Agent-2/Svc"],
            "only the dropping agent's claim is removed");
        _monitor.CanExecuteActions(collection).Should().BeTrue();
    }

    [Fact]
    public void IngestCollectionDropped_Ignored_WhenDatabaseNameMissing()
    {
        var collection = RemoteCollection();
        IngestWithAgent(collection, "Agent-1/Svc", "conn-1");

        _monitor.IngestCollectionDropped("Agent-1/Svc", collection.ConfigurationName.Value, databaseName: null, collection.CollectionName);

        _monitor.GetCollectionSources(collection.Key).Should().Contain("Agent-1/Svc",
            "an unidentifiable drop must not evict anything");
    }

    [Fact]
    public async Task IngestCollectionInfo_PreservesCollectionTypeName_ForRemoteDisplay()
    {
        // The Type can't cross the wire, but the name does — so the details dialog can still show it.
        _monitor.IngestCollectionInfo(new RemoteCollectionInfoDto
        {
            ConfigurationName = "Default",
            DatabaseName = "TestDb",
            CollectionName = "TestCol",
            SourceName = "Agent-1/Svc",
            Server = "remote-server:27017",
            Discovery = "Database",
            Registration = "Dynamic",
            EntityTypes = ["DynEntity"],
            CollectionTypeName = "DynRepositoryCollection",
        }, "conn-1");

        var result = await _monitor.GetInstanceAsync(new CollectionFingerprint
        {
            ConfigurationName = "Default",
            DatabaseName = "TestDb",
            CollectionName = "TestCol",
        });

        result.Should().NotBeNull();
        result.CollectionType.Should().BeNull("a remote collection's Type can't be reconstructed");
        result.CollectionTypeName.Should().Be("DynRepositoryCollection");
    }

    [Fact]
    public async Task IngestCollectionInfo_StampsReportedAt_AndFlagsRemoteOrigin()
    {
        var collection = RemoteCollection();
        IngestWithAgent(collection, "Agent-1/Svc", "conn-1");

        var result = await _monitor.GetInstanceAsync(collection);

        result.Should().NotBeNull();
        result.ReportedAt.Should().NotBeNull("ingest records the age of the data");
        result.Discovery.HasFlag(Discovery.Remote).Should().BeTrue("an agent report is remote-origin");
    }

    [Fact]
    public async Task SetClientCallForwarding_DispatchesToConnectedAgent_AndUpdatesStatus()
    {
        var collection = RemoteCollection();
        IngestWithAgent(collection, "Agent-1/Svc", "conn-1");
        _monitor.IngestClientStatus("Agent-1/Svc", new MonitorClientStatus { ForwardCompletedCalls = false, QueueMetricIntervalMs = 100 });

        _dispatcherMock
            .Setup(d => d.SetCallForwardingAsync("conn-1", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _monitor.SetClientCallForwardingAsync("Agent-1/Svc", true);

        result.Should().BeTrue();
        _dispatcherMock.Verify(d => d.SetCallForwardingAsync("conn-1", true, It.IsAny<CancellationToken>()), Times.Once);
        _monitor.GetMonitorClients().Single(c => c.SourceName == "Agent-1/Svc").Status.ForwardCompletedCalls.Should().BeTrue();
    }

    [Fact]
    public async Task SetClientCallForwarding_Throws_WhenAgentNotConnected()
    {
        var act = () => _monitor.SetClientCallForwardingAsync("Ghost/Svc", true);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not connected*");
    }

    [Fact]
    public async Task ResetAsync_ClearsRemoteCollectionsAndSources_AndBroadcasts()
    {
        var collection = RemoteCollection();
        IngestWithAgent(collection, "Agent-1/Svc", "conn-1");
        _monitor.GetCollectionSources(collection.Key).Should().NotBeEmpty();

        _dispatcherMock.Setup(d => d.ResetCacheAllAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _monitor.ResetAsync();

        _monitor.GetCollectionSources(collection.Key).Should().BeEmpty(
            "reset drops remotely-reported state so agents can re-send fresh info");
        _monitor.CanExecuteActions(collection).Should().BeFalse();
        _dispatcherMock.Verify(d => d.ResetCacheAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

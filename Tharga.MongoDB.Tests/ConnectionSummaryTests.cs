using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

public class ConnectionSummaryTests
{
    [Fact]
    public void ConnectionPoolMonitor_CountsOpenConnections_AndGuardsAgainstNegative()
    {
        var monitor = new ConnectionPoolMonitor();
        monitor.SetMaxPoolSize("cluster-A", 100);
        monitor.OnConnectionCreated("cluster-A");
        monitor.OnConnectionCreated("cluster-A");
        monitor.OnConnectionCreated("cluster-A");
        monitor.OnConnectionClosed("cluster-A");

        // close-before-create on another pool must not go negative
        monitor.OnConnectionClosed("cluster-B");

        var snapshot = monitor.GetSnapshot();
        snapshot.Single(p => p.ServerKey == "cluster-A").OpenConnections.Should().Be(2);
        snapshot.Single(p => p.ServerKey == "cluster-A").MaxPoolSize.Should().Be(100);
        snapshot.Single(p => p.ServerKey == "cluster-B").OpenConnections.Should().Be(0);
    }

    [Fact]
    public void GetClusterConnectionSummary_AggregatesLocalAndRemoteByCluster_WithLimit()
    {
        var connectionMonitor = new ConnectionPoolMonitor();
        connectionMonitor.SetMaxPoolSize("cluster-A", 100);
        for (var i = 0; i < 5; i++) connectionMonitor.OnConnectionCreated("cluster-A"); // local: 5 open

        var queueMonitor = new Mock<IQueueMonitor>();
        queueMonitor.Setup(q => q.GetPerPoolState()).Returns(new[]
        {
            new PoolQueueState { ServerKey = "cluster-A", ConfigurationNames = new[] { "main" }, QueueCount = 2, ExecutingCount = 4, LastWaitTimeMs = 0 },
        });

        var monitor = CreateMonitor("Local/App", queueMonitor.Object, connectionMonitor, clusterConnectionLimit: 3000);

        // A second source (agent) reports the same cluster + another cluster.
        monitor.IngestQueueMetric("Agent-1", new List<PoolMetricDto>
        {
            new() { ServerKey = "cluster-A", ConfigurationNames = new[] { "main" }, QueueCount = 7, ExecutingCount = 3, OpenConnections = 10, MaxPoolSize = 100 },
            new() { ServerKey = "cluster-B", ConfigurationNames = new[] { "reporting" }, QueueCount = 0, ExecutingCount = 0, OpenConnections = 3, MaxPoolSize = 50 },
        });

        var summary = monitor.GetClusterConnectionSummary();

        var a = summary.Single(s => s.Cluster == "cluster-A");
        a.OpenConnections.Should().Be(15);  // 5 local + 10 remote
        a.MaxConnections.Should().Be(200);   // 100 + 100 capacity
        a.SourceCount.Should().Be(2);        // Local/App + Agent-1
        a.Limit.Should().Be(3000);
        a.ConfigurationNames.Should().Contain("main");
        a.Pools.Should().HaveCount(1);
        var aPool = a.Pools.Single();
        aPool.MaxPoolSize.Should().Be(100);
        aPool.SourceCount.Should().Be(2);
        aPool.QueueCount.Should().Be(9);     // 2 local + 7 remote
        aPool.ExecutingCount.Should().Be(7); // 4 local + 3 remote
        aPool.Sources.Should().Contain(s => s.Source == "Local/App" && s.OpenConnections == 5 && s.QueueCount == 2 && s.ExecutingCount == 4);
        aPool.Sources.Should().Contain(s => s.Source == "Agent-1" && s.OpenConnections == 10 && s.QueueCount == 7 && s.ExecutingCount == 3);

        var b = summary.Single(s => s.Cluster == "cluster-B");
        b.OpenConnections.Should().Be(3);
        b.SourceCount.Should().Be(1);
        b.ConfigurationNames.Should().Contain("reporting");
    }

    [Fact]
    public void GetClusterConnectionSummary_TwoPoolsSameCluster_GroupUnderOneClusterAsSeparatePools()
    {
        // Same host, two different max-pool-sizes => two server-keys => one cluster, two pools.
        var connectionMonitor = new ConnectionPoolMonitor();
        connectionMonitor.SetMaxPoolSize("localhost:27017|pool=100", 100);
        for (var i = 0; i < 5; i++) connectionMonitor.OnConnectionCreated("localhost:27017|pool=100");
        connectionMonitor.SetMaxPoolSize("localhost:27017|pool=25", 25);
        for (var i = 0; i < 3; i++) connectionMonitor.OnConnectionCreated("localhost:27017|pool=25");

        var queueMonitor = new Mock<IQueueMonitor>();
        queueMonitor.Setup(q => q.GetPerPoolState()).Returns(new[]
        {
            new PoolQueueState { ServerKey = "localhost:27017|pool=100", ConfigurationNames = new[] { "Core" }, QueueCount = 0, ExecutingCount = 0, LastWaitTimeMs = 0 },
            new PoolQueueState { ServerKey = "localhost:27017|pool=25", ConfigurationNames = new[] { "Reporting" }, QueueCount = 0, ExecutingCount = 0, LastWaitTimeMs = 0 },
        });

        var monitor = CreateMonitor("Local/App", queueMonitor.Object, connectionMonitor, clusterConnectionLimit: 3000);

        var summary = monitor.GetClusterConnectionSummary();

        summary.Should().HaveCount(1);
        var cluster = summary.Single();
        cluster.Cluster.Should().Be("localhost:27017");
        cluster.OpenConnections.Should().Be(8);    // 5 + 3 across both pools
        cluster.MaxConnections.Should().Be(125);   // 100 + 25
        cluster.SourceCount.Should().Be(1);         // one process, two pools
        cluster.Pools.Should().HaveCount(2);
        cluster.Pools.Should().Contain(p => p.MaxPoolSize == 100 && p.OpenConnections == 5 && p.ConfigurationNames.Contains("Core"));
        cluster.Pools.Should().Contain(p => p.MaxPoolSize == 25 && p.OpenConnections == 3 && p.ConfigurationNames.Contains("Reporting"));
    }

    [Fact]
    public void GetClusterConnectionSummary_DifferentHosts_AreDistinctClusters()
    {
        var connectionMonitor = new ConnectionPoolMonitor();
        connectionMonitor.SetMaxPoolSize("localhost:27017|pool=100", 100);
        connectionMonitor.OnConnectionCreated("localhost:27017|pool=100");

        var queueMonitor = new Mock<IQueueMonitor>();
        queueMonitor.Setup(q => q.GetPerPoolState()).Returns(Array.Empty<PoolQueueState>());

        var monitor = CreateMonitor("Local/App", queueMonitor.Object, connectionMonitor, clusterConnectionLimit: null);

        monitor.IngestQueueMetric("Agent-1", new List<PoolMetricDto>
        {
            new() { ServerKey = "127.0.0.1:27017|pool=100", ConfigurationNames = new[] { "Archive" }, QueueCount = 0, ExecutingCount = 0, OpenConnections = 2, MaxPoolSize = 100 },
        });

        var summary = monitor.GetClusterConnectionSummary();

        summary.Select(s => s.Cluster).Should().BeEquivalentTo(new[] { "localhost:27017", "127.0.0.1:27017" });
    }

    [Fact]
    public void GetClusterConnectionSummary_ResolverSetsLimitPerCluster_FallsBackToGlobalThenNull()
    {
        var connectionMonitor = new ConnectionPoolMonitor();
        connectionMonitor.SetMaxPoolSize("cluster0.ab12.mongodb.net:27017|pool=100", 100);
        connectionMonitor.OnConnectionCreated("cluster0.ab12.mongodb.net:27017|pool=100");
        connectionMonitor.SetMaxPoolSize("localhost:27017|pool=100", 100);
        connectionMonitor.OnConnectionCreated("localhost:27017|pool=100");

        var queueMonitor = new Mock<IQueueMonitor>();
        queueMonitor.Setup(q => q.GetPerPoolState()).Returns(Array.Empty<PoolQueueState>());

        // Resolver: Atlas clusters get 3000, everything else falls through (null) to the global fallback.
        var monitor = CreateMonitor("Local/App", queueMonitor.Object, connectionMonitor, clusterConnectionLimit: null,
            resolver: (_, ctx) => ctx.IsAtlas ? 3000 : (int?)null);

        var summary = monitor.GetClusterConnectionSummary();

        summary.Single(s => s.Cluster == "cluster0.ab12.mongodb.net:27017").Limit.Should().Be(3000);
        summary.Single(s => s.Cluster == "localhost:27017").Limit.Should().BeNull(); // resolver returned null, no global -> no bar
    }

    [Fact]
    public void GetClusterConnectionSummary_NoResolverNoGlobal_LeavesLimitNull()
    {
        var connectionMonitor = new ConnectionPoolMonitor();
        connectionMonitor.SetMaxPoolSize("localhost:27017|pool=100", 100);
        connectionMonitor.OnConnectionCreated("localhost:27017|pool=100");

        var queueMonitor = new Mock<IQueueMonitor>();
        queueMonitor.Setup(q => q.GetPerPoolState()).Returns(Array.Empty<PoolQueueState>());

        var monitor = CreateMonitor("Local/App", queueMonitor.Object, connectionMonitor, clusterConnectionLimit: null);

        monitor.GetClusterConnectionSummary().Single().Limit.Should().BeNull();
    }

    [Fact]
    public void TwoInstancesSameSourceName_StaySeparate_AndReconnectReattaches()
    {
        var connectionMonitor = new ConnectionPoolMonitor();
        var queueMonitor = new Mock<IQueueMonitor>();
        queueMonitor.Setup(q => q.GetPerPoolState()).Returns(Array.Empty<PoolQueueState>());
        var monitor = CreateMonitor("Server", queueMonitor.Object, connectionMonitor, clusterConnectionLimit: null);

        var instanceA = Guid.NewGuid();
        var instanceB = Guid.NewGuid();
        monitor.IngestClientConnected(Client(instanceA, "conn-A"));
        monitor.IngestClientConnected(Client(instanceB, "conn-B"));

        // Both processes report the SAME source name, on different connections.
        var pool = new PoolMetricDto { ServerKey = "host:27017|pool=100", ConfigurationNames = new[] { "Core" }, QueueCount = 0, ExecutingCount = 0, OpenConnections = 3, MaxPoolSize = 100 };
        monitor.IngestQueueMetric("PC/App", new[] { pool }, "conn-A");
        monitor.IngestQueueMetric("PC/App", new[] { pool }, "conn-B");

        // The cluster's pool now carries two distinct sources (one per instance), not one merged.
        monitor.GetClusterConnectionSummary().Single().Pools.Single().SourceCount.Should().Be(2);

        // Reconnect instance A on a new connection id — same Instance keeps the same effective source (still 2).
        monitor.IngestClientConnected(Client(instanceA, "conn-A2"));
        monitor.IngestQueueMetric("PC/App", new[] { pool }, "conn-A2");
        monitor.GetClusterConnectionSummary().Single().Pools.Single().SourceCount.Should().Be(2);
    }

    private static MonitorClientDto Client(Guid instance, string connectionId) => new()
    {
        Instance = instance,
        ConnectionId = connectionId,
        Machine = "PC",
        Type = "App",
        Version = "1.0",
        IsConnected = true,
        ConnectTime = DateTime.UtcNow,
    };

    private static DatabaseMonitor CreateMonitor(string sourceName, IQueueMonitor queueMonitor, IConnectionPoolMonitor connectionMonitor, int? clusterConnectionLimit,
        Func<IServiceProvider, ClusterConnectionLimitContext, int?> resolver = null)
    {
        var factoryMock = new Mock<IMongoDbServiceFactory>();
        factoryMock.Setup(f => f.SourceName).Returns(sourceName);

        var instanceMock = new Mock<IMongoDbInstance>();
        instanceMock.Setup(i => i.RegisteredCollections).Returns(new ConcurrentDictionary<Type, Type>());

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var callLibrary = new CallLibrary(Options.Create(new DatabaseOptions { Monitor = new MonitorOptions() }));
        var cacheMock = new Mock<ICollectionCache>();
        cacheMock.Setup(c => c.LoadAsync()).Returns(Task.CompletedTask);
        cacheMock.Setup(c => c.GetKeys()).Returns(Array.Empty<string>());
        cacheMock.Setup(c => c.GetAll()).Returns(Array.Empty<CollectionInfo>());

        var monitor = new DatabaseMonitor(
            factoryMock.Object,
            instanceMock.Object,
            serviceProvider,
            new Mock<IRepositoryConfiguration>().Object,
            new Mock<ICollectionProvider>().Object,
            callLibrary,
            cacheMock.Object,
            queueMonitor,
            connectionMonitor,
            Options.Create(new DatabaseOptions { Monitor = new MonitorOptions { ClusterConnectionLimit = clusterConnectionLimit, ClusterConnectionLimitResolver = resolver } }),
            NullLogger<DatabaseMonitor>.Instance);
        monitor.Start(serviceProvider);
        return monitor;
    }
}

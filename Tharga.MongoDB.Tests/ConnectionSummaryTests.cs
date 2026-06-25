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
            new PoolQueueState { ServerKey = "cluster-A", ConfigurationNames = new[] { "main" }, QueueCount = 0, ExecutingCount = 0, LastWaitTimeMs = 0 },
        });

        var monitor = CreateMonitor("Local/App", queueMonitor.Object, connectionMonitor, clusterConnectionLimit: 3000);

        // A second source (agent) reports the same cluster + another cluster.
        monitor.IngestQueueMetric("Agent-1", new List<PoolMetricDto>
        {
            new() { ServerKey = "cluster-A", ConfigurationNames = new[] { "main" }, QueueCount = 0, ExecutingCount = 0, OpenConnections = 10, MaxPoolSize = 100 },
            new() { ServerKey = "cluster-B", ConfigurationNames = new[] { "reporting" }, QueueCount = 0, ExecutingCount = 0, OpenConnections = 3, MaxPoolSize = 50 },
        });

        var summary = monitor.GetClusterConnectionSummary();

        var a = summary.Single(s => s.ServerKey == "cluster-A");
        a.OpenConnections.Should().Be(15);  // 5 local + 10 remote
        a.MaxConnections.Should().Be(200);   // 100 + 100 capacity
        a.SourceCount.Should().Be(2);        // Local/App + Agent-1
        a.Limit.Should().Be(3000);
        a.ConfigurationNames.Should().Contain("main");

        var b = summary.Single(s => s.ServerKey == "cluster-B");
        b.OpenConnections.Should().Be(3);
        b.SourceCount.Should().Be(1);
        b.ConfigurationNames.Should().Contain("reporting");
    }

    private static DatabaseMonitor CreateMonitor(string sourceName, IQueueMonitor queueMonitor, IConnectionPoolMonitor connectionMonitor, int? clusterConnectionLimit)
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
            Options.Create(new DatabaseOptions { Monitor = new MonitorOptions { ClusterConnectionLimit = clusterConnectionLimit } }),
            NullLogger<DatabaseMonitor>.Instance);
        monitor.Start(serviceProvider);
        return monitor;
    }
}

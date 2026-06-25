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

public class PerPoolQueueMetricTests
{
    // --- ExecuteLimiter: per-pool split + configuration tagging ---

    private static ExecuteLimiter CreateLimiter() =>
        new(Mock.Of<IOptions<ExecuteLimiterOptions>>(x => x.Value == new ExecuteLimiterOptions { Enabled = true }),
            NullLogger<ExecuteLimiter>.Instance);

    [Fact]
    public async Task GetPerPoolState_SplitsByServerKey_AndTagsConfiguration()
    {
        var limiter = CreateLimiter();

        await limiter.ExecuteAsync(_ => Task.FromResult(0), "cluster-A", "cfgA", 100, CancellationToken.None);
        await limiter.ExecuteAsync(_ => Task.FromResult(0), "cluster-B", "cfgB", 100, CancellationToken.None);

        var pools = limiter.GetPerPoolState();

        pools.Should().HaveCount(2);
        pools.Single(p => p.ServerKey == "cluster-A").ConfigurationNames.Should().BeEquivalentTo("cfgA");
        pools.Single(p => p.ServerKey == "cluster-B").ConfigurationNames.Should().BeEquivalentTo("cfgB");
    }

    [Fact]
    public async Task GetPerPoolState_TwoConfigurationsOnSameCluster_CollapseToOnePoolWithBothNames()
    {
        var limiter = CreateLimiter();

        await limiter.ExecuteAsync(_ => Task.FromResult(0), "shared-cluster", "cfg1", 100, CancellationToken.None);
        await limiter.ExecuteAsync(_ => Task.FromResult(0), "shared-cluster", "cfg2", 100, CancellationToken.None);

        var pools = limiter.GetPerPoolState();

        pools.Should().ContainSingle();
        pools[0].ServerKey.Should().Be("shared-cluster");
        pools[0].ConfigurationNames.Should().BeEquivalentTo("cfg1", "cfg2");
    }

    // --- DatabaseMonitor: GetPerPoolQueueState labeling + remote round-trip ---

    private static DatabaseMonitor CreateMonitor(string sourceName, IQueueMonitor queueMonitor)
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
            Options.Create(new DatabaseOptions { Monitor = new MonitorOptions() }),
            NullLogger<DatabaseMonitor>.Instance);
        monitor.Start(serviceProvider);
        return monitor;
    }

    private static IQueueMonitor StubQueueMonitor(params PoolQueueState[] pools)
    {
        var mock = new Mock<IQueueMonitor>();
        mock.Setup(q => q.GetPerPoolState()).Returns(pools);
        return mock.Object;
    }

    [Fact]
    public void GetPerPoolQueueState_SingleSource_LabelsByConfigurationName()
    {
        var monitor = CreateMonitor("Local/App", StubQueueMonitor(new PoolQueueState
        {
            ServerKey = "cluster-A",
            ConfigurationNames = new[] { "main" },
            QueueCount = 3,
            ExecutingCount = 1,
            LastWaitTimeMs = 12,
        }));

        var state = monitor.GetPerPoolQueueState();

        state.Should().ContainKey("Local/App::cluster-A");
        var pool = state["Local/App::cluster-A"];
        pool.Label.Should().Be("main"); // single source -> no source suffix
        pool.QueueCount.Should().Be(3);
        pool.ExecutingCount.Should().Be(1);
    }

    [Fact]
    public void GetPerPoolQueueState_RemotePoolsRoundTrip_AndDisambiguateBySource()
    {
        var monitor = CreateMonitor("Local/App", StubQueueMonitor(new PoolQueueState
        {
            ServerKey = "cluster-A",
            ConfigurationNames = new[] { "main" },
            QueueCount = 1,
            ExecutingCount = 0,
            LastWaitTimeMs = 0,
        }));

        // Simulate what MonitorQueueMetricHandler does for a per-pool message from a remote agent.
        monitor.IngestQueueMetric("Agent-1/Svc", new List<PoolMetricDto>
        {
            new() { ServerKey = "cluster-B", ConfigurationNames = new[] { "reporting" }, QueueCount = 5, ExecutingCount = 2, WaitTimeMs = 40 },
        });

        var state = monitor.GetPerPoolQueueState();

        // Two sources now report -> labels are source-suffixed for distinctness.
        state["Local/App::cluster-A"].Label.Should().Be("main @ Local/App");
        var remote = state["Agent-1/Svc::cluster-B"];
        remote.Label.Should().Be("reporting @ Agent-1/Svc");
        remote.QueueCount.Should().Be(5);
        remote.ExecutingCount.Should().Be(2);
        remote.LastWaitTimeMs.Should().Be(40);
    }
}

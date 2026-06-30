using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;
using Tharga.MongoDB.Monitor.Server;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class MonitorLibraryVersionTests
{
    private static DatabaseMonitor CreateStartedMonitor()
    {
        var factoryMock = new Mock<IMongoDbServiceFactory>();
        factoryMock.Setup(f => f.SourceName).Returns("TestServer/TestApp");

        var instanceMock = new Mock<IMongoDbInstance>();
        instanceMock.Setup(i => i.RegisteredCollections).Returns(new ConcurrentDictionary<Type, Type>());

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var options = Options.Create(new DatabaseOptions { Monitor = new MonitorOptions() });
        var callLibrary = new CallLibrary(options);

        var monitor = new DatabaseMonitor(
            factoryMock.Object,
            instanceMock.Object,
            serviceProvider,
            new Mock<IRepositoryConfiguration>().Object,
            new Mock<ICollectionProvider>().Object,
            callLibrary,
            new MemoryCollectionCache(),
            new Mock<IQueueMonitor>().Object,
            new ConnectionPoolMonitor(),
            options,
            NullLogger<DatabaseMonitor>.Instance);

        monitor.Start(serviceProvider);
        return monitor;
    }

    [Fact]
    public void IngestClientStatus_CarriesLibraryVersion_ToMonitorClients()
    {
        var monitor = CreateStartedMonitor();
        monitor.IngestClientConnected(new MonitorClientDto
        {
            Instance = Guid.NewGuid(),
            ConnectionId = "conn-1",
            Machine = "Agent-Machine",
            Type = "TestAgent",
            Version = "host-app-9.9",
            IsConnected = true,
            ConnectTime = DateTime.UtcNow,
            SourceName = "Agent-1/Svc",
        });

        monitor.IngestClientStatus("Agent-1/Svc", new MonitorClientStatus
        {
            ForwardCompletedCalls = false,
            QueueMetricIntervalMs = 1000,
            LibraryVersion = "2.13.0",
        }, "conn-1");

        var client = monitor.GetMonitorClients().Single(c => c.SourceName == "Agent-1/Svc");
        client.Status.Should().NotBeNull();
        client.Status.LibraryVersion.Should().Be("2.13.0", "the agent's monitor-client library version is distinct from its host-app Version");
        client.Version.Should().Be("host-app-9.9");
    }

    [Fact]
    public void MonitorServerInfo_ReportsNonEmptyLibraryVersion()
    {
        IMonitorServerInfo info = new MonitorServerInfo();

        info.LibraryVersion.Should().NotBeNullOrEmpty();
        info.LibraryVersion.Should().NotContain("+");
    }
}

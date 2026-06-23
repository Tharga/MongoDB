using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.HealthChecks;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Covers the resilient-startup-connectivity feature (issue #123): option defaults, the
/// <see cref="MongoDbConnectivityState"/> probe (aggregation, retry, never-throws), the
/// failure/exception types, and the <see cref="MongoDbHealthCheck"/>.
/// </summary>
public class StartupConnectivityTests
{
    // --- UseMongoOptions defaults ---

    [Fact]
    public void StartupConnectivity_DefaultsToFailFast()
    {
        new UseMongoOptions().StartupConnectivity.Should().Be(StartupConnectivityMode.FailFast);
    }

    [Fact]
    public void StartupConnectivityRetryCount_DefaultsToThree()
    {
        new UseMongoOptions().StartupConnectivityRetryCount.Should().Be(3);
    }

    [Fact]
    public void StartupConnectivityRetryDelay_DefaultsToTwoSeconds()
    {
        new UseMongoOptions().StartupConnectivityRetryDelay.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void StartupFailureCallback_DefaultsToNull()
    {
        new UseMongoOptions().StartupFailureCallback.Should().BeNull();
    }

    // --- MongoDbConnectivityState ---

    [Fact]
    public async Task CheckAsync_AllReachable_IsHealthy()
    {
        var state = CreateState(new()
        {
            ["Default"] = Service(Info(true, "ok-default")),
            ["Integration"] = Service(Info(true, "ok-integration")),
        });

        var results = await state.CheckAsync();

        state.IsHealthy.Should().BeTrue();
        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.CanConnect);
        state.Connections.Single(c => c.ConfigurationName == "Default").Message.Should().Be("ok-default");
    }

    [Fact]
    public async Task CheckAsync_OneUnreachable_IsNotHealthy_AndCarriesMessage()
    {
        var state = CreateState(new()
        {
            ["Default"] = Service(Info(true, "ok")),
            ["Integration"] = Service(Info(false, "server selection timed out")),
        });

        await state.CheckAsync();

        state.IsHealthy.Should().BeFalse();
        var failing = state.Connections.Single(c => !c.CanConnect);
        failing.ConfigurationName.Should().Be("Integration");
        failing.Message.Should().Be("server selection timed out");
    }

    [Fact]
    public async Task CheckWithRetry_TransientFailureThenSuccess_EndsHealthy()
    {
        // First probe fails, second succeeds — with 3 attempts and a tiny delay it should recover.
        var state = CreateState(new()
        {
            ["Default"] = Service(Info(false, "blip"), Info(true, "recovered")),
        });

        var results = await state.CheckWithRetryAsync(attempts: 3, initialDelay: TimeSpan.FromMilliseconds(1), assureFirewall: false);

        state.IsHealthy.Should().BeTrue();
        results.Single().Message.Should().Be("recovered");
    }

    [Fact]
    public async Task CheckWithRetry_HealthyConnectionIsNotReprobed()
    {
        var service = new Mock<IMongoDbService>();
        service.Setup(s => s.GetInfoAsync(It.IsAny<bool>())).ReturnsAsync(Info(true, "ok"));
        var state = CreateState(new() { ["Default"] = service.Object });

        await state.CheckWithRetryAsync(attempts: 3, initialDelay: TimeSpan.FromMilliseconds(1), assureFirewall: false);

        // A reachable connection must be probed exactly once even though 3 attempts were allowed.
        service.Verify(s => s.GetInfoAsync(It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public async Task CheckAsync_ProbeThrows_ReportedAsUnreachable_NeverThrows()
    {
        var service = new Mock<IMongoDbService>();
        service.Setup(s => s.GetInfoAsync(It.IsAny<bool>())).ThrowsAsync(new Exception("resolution blew up"));
        var state = CreateState(new() { ["Default"] = service.Object });

        var results = await state.CheckAsync();

        results.Single().CanConnect.Should().BeFalse();
        results.Single().Message.Should().Be("resolution blew up");
        state.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public void IsHealthy_BeforeAnyCheck_IsTrue()
    {
        // Nothing has been observed as unreachable yet.
        var state = CreateState(new() { ["Default"] = Service(Info(true, "ok")) });

        state.IsHealthy.Should().BeTrue();
        state.Connections.Should().BeEmpty();
    }

    // --- MongoStartupFailure / MongoStartupConnectivityException ---

    [Fact]
    public void MongoStartupFailure_Summary_ListsConfigsAndMessages()
    {
        var failure = new MongoStartupFailure(new[]
        {
            Conn("Default", false, "timeout-a"),
            Conn("Integration", false, "timeout-b"),
        });

        failure.UnreachableConnections.Should().HaveCount(2);
        failure.Summary.Should().Contain("Default: timeout-a");
        failure.Summary.Should().Contain("Integration: timeout-b");
    }

    [Fact]
    public void MongoStartupConnectivityException_CarriesFailure_AndDescribesIt()
    {
        var failure = new MongoStartupFailure(new[] { Conn("Default", false, "timeout") });

        var ex = new MongoStartupConnectivityException(failure);

        ex.Failure.Should().BeSameAs(failure);
        ex.Message.Should().Contain("Default: timeout");
        ex.Message.Should().Contain("1 configuration");
    }

    // --- MongoDbHealthCheck ---

    [Fact]
    public async Task HealthCheck_AllReachable_ReportsHealthy()
    {
        var connections = new[] { Conn("Default", true, "reachable") };
        var check = new MongoDbHealthCheck(StubState(connections), live: false);

        var result = await check.CheckHealthAsync(Context());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task HealthCheck_OneUnreachable_ReportsFailureStatus()
    {
        var connections = new[] { Conn("Default", true, "reachable"), Conn("Integration", false, "down") };
        var check = new MongoDbHealthCheck(StubState(connections), live: false);

        var result = await check.CheckHealthAsync(Context());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Integration: down");
        result.Data.Should().ContainKey("Integration");
    }

    [Fact]
    public async Task HealthCheck_Live_ReprobesViaCheckAsync()
    {
        var connections = new[] { Conn("Default", true, "reachable") };
        var state = new Mock<IMongoDbConnectivityState>();
        state.Setup(s => s.CheckAsync(It.IsAny<CancellationToken>())).ReturnsAsync(connections);
        var check = new MongoDbHealthCheck(state.Object, live: true);

        await check.CheckHealthAsync(Context());

        state.Verify(s => s.CheckAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- helpers ---

    private static DatabaseInfo Info(bool canConnect, string message)
        => new() { CanConnect = canConnect, Message = message };

    private static ConnectionConnectivity Conn(string config, bool canConnect, string message)
        => new() { ConfigurationName = config, CanConnect = canConnect, Message = message, CheckedAt = DateTime.UtcNow };

    private static IMongoDbService Service(params DatabaseInfo[] sequence)
    {
        var mock = new Mock<IMongoDbService>();
        var setup = mock.SetupSequence(s => s.GetInfoAsync(It.IsAny<bool>()));
        foreach (var info in sequence)
            setup = setup.ReturnsAsync(info);
        return mock.Object;
    }

    private static MongoDbConnectivityState CreateState(Dictionary<string, IMongoDbService> servicesByConfig)
    {
        var factory = new Mock<IMongoDbServiceFactory>();
        factory.Setup(f => f.GetMongoDbService(It.IsAny<Func<DatabaseContext>>()))
            .Returns((Func<DatabaseContext> loader) => servicesByConfig[loader().ConfigurationName.Value]);

        var config = new Mock<IRepositoryConfiguration>();
        config.Setup(c => c.GetDatabaseConfigurationNames()).Returns(servicesByConfig.Keys.ToArray());

        return new MongoDbConnectivityState(factory.Object, config.Object, NullLogger<MongoDbConnectivityState>.Instance);
    }

    private static IMongoDbConnectivityState StubState(IReadOnlyList<ConnectionConnectivity> connections)
    {
        var state = new Mock<IMongoDbConnectivityState>();
        state.SetupGet(s => s.Connections).Returns(connections);
        state.Setup(s => s.CheckAsync(It.IsAny<CancellationToken>())).ReturnsAsync(connections);
        return state.Object;
    }

    private static HealthCheckContext Context()
        => new() { Registration = new HealthCheckRegistration("mongodb", Mock.Of<IHealthCheck>(), HealthStatus.Unhealthy, tags: null) };
}

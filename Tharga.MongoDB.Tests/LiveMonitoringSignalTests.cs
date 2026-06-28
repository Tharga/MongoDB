using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Communication.MessageHandler;
using Tharga.MongoDB.Monitor.Client;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Covers the explicit live-monitoring signal that replaces reliance on the framework's
/// HasSubscribers tracker: the server-pushed <see cref="SetLiveMonitoringActiveMessage"/> is handled
/// by <see cref="SetLiveMonitoringActiveHandler"/>, which flips the local <see cref="LiveMonitoringState"/>
/// that the queue tick gates on.
/// </summary>
public class LiveMonitoringSignalTests
{
    private static IConfiguration EmptyConfiguration()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>()).Build();

    [Fact]
    public async Task Handler_SetsActiveTrue_OnActiveMessage()
    {
        var state = new LiveMonitoringState();
        var serviceProvider = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
        var handler = new SetLiveMonitoringActiveHandler(serviceProvider);

        await handler.Handle(new SetLiveMonitoringActiveMessage { Active = true });

        state.Active.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_SetsActiveFalse_OnInactiveMessage()
    {
        var state = new LiveMonitoringState { Active = true };
        var serviceProvider = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
        var handler = new SetLiveMonitoringActiveHandler(serviceProvider);

        await handler.Handle(new SetLiveMonitoringActiveMessage { Active = false });

        state.Active.Should().BeFalse();
    }

    [Fact]
    public async Task Handler_WithoutState_DoesNotThrow()
    {
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var handler = new SetLiveMonitoringActiveHandler(serviceProvider);

        var act = async () => await handler.Handle(new SetLiveMonitoringActiveMessage { Active = true });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Registration_RegistersLiveMonitoringState_AsSingleton()
    {
        var services = new ServiceCollection();

        services.AddMongoDbMonitorClient(EmptyConfiguration(), sendTo: "https://hub.example/monitor");

        services.Should().Contain(d => d.ServiceType == typeof(LiveMonitoringState) && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void Registration_RegistersSetLiveMonitoringActiveHandler_InHandlerTypeService()
    {
        var services = new ServiceCollection();

        services.AddMongoDbMonitorClient(EmptyConfiguration(), sendTo: "https://hub.example/monitor");

        var handlerTypes = services.BuildServiceProvider().GetRequiredService<IHandlerTypeService>();
        handlerTypes.TryGetHandler(typeof(SetLiveMonitoringActiveMessage), out _).Should().BeTrue(
            "the agent must handle the server's explicit live-monitoring signal");
    }
}

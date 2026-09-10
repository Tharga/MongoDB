using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;
using MongoDB.Driver.Core.Events;
using Moq;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests.Diagnostics;

/// <summary>
/// Regression coverage for GitHub issue #157. <c>MongoDbClientProvider</c> used to <b>assign</b>
/// <c>settings.ClusterConfigurator</c>, so there was nowhere for a consumer to attach a driver event
/// subscriber, and any hook added later would have replaced the built-in command and connection-pool
/// subscriptions rather than joining them. The configurator is now composed from an ordered step list.
/// </summary>
public class ClusterConfiguratorCompositionTests
{
    private const string ServerKey = "localhost:27017|pool=100";

    private static CommandMonitorService BuildCommandMonitor() => new(Mock.Of<ILogger<CommandMonitorService>>());

    private static MongoDbClientProvider BuildProvider(params Action<ClusterBuilder>[] consumerCallbacks)
    {
        return new MongoDbClientProvider(BuildCommandMonitor(), Mock.Of<IConnectionPoolMonitor>(), consumerCallbacks);
    }

    [Fact]
    public void WithNoExistingConfigurator_BuiltInStepComesFirst()
    {
        var sut = BuildProvider();

        var steps = sut.BuildConfiguratorSteps(null, ServerKey);

        steps.Select(x => x.Kind).Should().Equal(ClusterConfiguratorStepKind.BuiltIn);
    }

    [Fact]
    public void ExistingConfigurator_IsPreserved_AndRunsBeforeTheBuiltIns()
    {
        var sut = BuildProvider();

        var steps = sut.BuildConfiguratorSteps(_ => { }, ServerKey);

        steps.Select(x => x.Kind).Should().Equal(ClusterConfiguratorStepKind.Existing, ClusterConfiguratorStepKind.BuiltIn);
    }

    [Fact]
    public void ConsumerCallbacks_RunAfterTheBuiltIns_InRegistrationOrder()
    {
        var order = new List<string>();
        var sut = BuildProvider(_ => order.Add("first"), _ => order.Add("second"), _ => order.Add("third"));

        var steps = sut.BuildConfiguratorSteps(null, ServerKey);
        foreach (var step in steps.Where(x => x.Kind == ClusterConfiguratorStepKind.Consumer)) step.Configure(null);

        steps.Select(x => x.Kind).Should().Equal(
            ClusterConfiguratorStepKind.BuiltIn,
            ClusterConfiguratorStepKind.Consumer,
            ClusterConfiguratorStepKind.Consumer,
            ClusterConfiguratorStepKind.Consumer);
        order.Should().Equal("first", "second", "third");
    }

    [Fact]
    public void ConsumerCallback_DoesNotDisplaceTheBuiltInStep()
    {
        // The failure mode the issue names: a consumer hook that silently turns the monitor off.
        var sut = BuildProvider(_ => { });

        var steps = sut.BuildConfiguratorSteps(null, ServerKey);

        steps.Should().Contain(x => x.Kind == ClusterConfiguratorStepKind.BuiltIn);
    }

    [Fact]
    public void WithNothingToConfigure_ThereAreNoSteps()
    {
        var sut = new MongoDbClientProvider();

        var steps = sut.BuildConfiguratorSteps(null, ServerKey);

        steps.Should().BeEmpty();
    }

    [Fact]
    public void EveryStep_SurvivesARealClusterBuilder()
    {
        var consumerRan = false;
        var sut = BuildProvider(cb => { cb.Subscribe<CommandStartedEvent>(_ => { }); consumerRan = true; });

        var steps = sut.BuildConfiguratorSteps(null, ServerKey);
        var builder = new ClusterBuilder();
        foreach (var step in steps) step.Configure(builder);

        consumerRan.Should().BeTrue();
    }

    [Fact]
    public void GetClient_AppliesTheComposedConfigurator()
    {
        // End-to-end: the driver invokes the configurator while constructing the client, so this proves the
        // composition reaches a real MongoClient rather than only being built correctly.
        var consumerRan = false;
        var sut = BuildProvider(_ => consumerRan = true);

        sut.GetClient(new MongoUrl("mongodb://localhost:27017/Tharga_MongoDb_ConfiguratorComposition"));

        consumerRan.Should().BeTrue();
    }
}

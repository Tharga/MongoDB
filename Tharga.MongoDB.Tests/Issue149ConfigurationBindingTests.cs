using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.MongoDB.Configuration;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Regression coverage for GitHub issue #149. <c>AddMongoDB</c> bound the <c>MongoDB</c> section into a
/// <c>DatabaseOptions</c> and then hand-copied a fixed subset of properties into the instance it registered.
/// Everything outside that list was bound and then dropped — silently, so a value in appsettings.json simply
/// never took effect, and the list went stale each time an option was added. It now binds onto the instance.
/// </summary>
public class Issue149ConfigurationBindingTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string> values = null)
    {
        var builder = new ConfigurationBuilder();
        if (values != null) builder.AddInMemoryCollection(values);
        return builder.Build();
    }

    private static IServiceProvider Register(IConfiguration config, Action<DatabaseOptions> options = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddMongoDB(config, o =>
        {
            o.AutoRegisterRepositories = false;
            o.AutoRegisterCollections = false;
            options?.Invoke(o);
        });
        return services.BuildServiceProvider();
    }

    private static DatabaseOptions Resolve(IServiceProvider provider)
    {
        return provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    }

    // --- The properties the hand-copy dropped ---

    [Fact]
    public void MonitorOptions_PropertiesOutsideTheOldCopyList_TakeEffectFromAppSettings()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "MongoDB:Monitor:CallRecordingLevel", "WhenConsumed" },
            { "MongoDB:Monitor:StorageMode", "Memory" },
            { "MongoDB:Monitor:SourceName", "agent-1" },
            { "MongoDB:Monitor:SendTo", "https://monitor.example.com" },
            { "MongoDB:Monitor:ForwardCompletedCalls", "true" },
            { "MongoDB:Monitor:QueueMetricInterval", "00:00:30" },
            { "MongoDB:Monitor:EnableCommandMonitoring", "true" },
            { "MongoDB:Monitor:ClusterConnectionLimit", "42" },
        });

        var result = Resolve(Register(config)).Monitor;

        result.CallRecordingLevel.Should().Be(CallRecordingLevel.WhenConsumed);
        result.StorageMode.Should().Be(MonitorStorageMode.Memory);
        result.SourceName.Should().Be("agent-1");
        result.SendTo.Should().Be("https://monitor.example.com");
        result.ForwardCompletedCalls.Should().BeTrue();
        result.QueueMetricInterval.Should().Be(TimeSpan.FromSeconds(30));
        result.EnableCommandMonitoring.Should().BeTrue();
        result.ClusterConnectionLimit.Should().Be(42);
    }

    [Fact]
    public void DatabaseOptions_PropertiesOutsideTheOldCopyList_TakeEffectFromAppSettings()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "MongoDB:AssureIndexAtStartup", "true" },
            { "MongoDB:AllowDelayedCommit", "false" },
            { "MongoDB:FailedIndexRecheckInterval", "00:15:00" },
            { "MongoDB:Quilt4NetHeartbeatInterval", "00:02:00" },
        });

        var result = Resolve(Register(config));

        result.AssureIndexAtStartup.Should().BeTrue();
        result.AllowDelayedCommit.Should().BeFalse();
        result.FailedIndexRecheckInterval.Should().Be(TimeSpan.FromMinutes(15));
        result.Quilt4NetHeartbeatInterval.Should().Be(TimeSpan.FromMinutes(2));
    }

    /// <summary>
    /// The concrete symptom named in the issue. CallRecordingLevel set in configuration reached neither the
    /// copy nor the registered MonitorRecordingState, so the level came back OnDemand.
    /// </summary>
    [Fact]
    public void CallRecordingLevel_FromAppSettings_ReachesTheRegisteredMonitorRecordingState()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "MongoDB:Monitor:CallRecordingLevel", "WhenConsumed" },
        });

        var provider = Register(config);

        provider.GetRequiredService<MonitorRecordingState>().Level.Should().Be(CallRecordingLevel.WhenConsumed);
    }

    // --- Nothing that already worked may regress ---

    /// <summary>
    /// DefaultConfigurationName is the one property with no initialiser of its own, so binding alone would
    /// have left it null — and DatabaseMonitor uses it as the terminal fallback when resolving a
    /// configuration name. It carries its default on the property now.
    /// </summary>
    [Fact]
    public void DefaultConfigurationName_NoConfiguration_StillFallsBackToDefault()
    {
        Resolve(Register(BuildConfig())).DefaultConfigurationName.Should().Be("Default");
        new DatabaseOptions().DefaultConfigurationName.Should().Be("Default");
    }

    [Fact]
    public void DefaultConfigurationName_AppSettingsConfigured_UsesAppSettingsValue()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "MongoDB:DefaultConfigurationName", "Secondary" },
        });

        Resolve(Register(config)).DefaultConfigurationName.Should().Be("Secondary");
    }

    [Fact]
    public void NoConfiguration_LeavesEveryPropertyAtItsDeclaredDefault()
    {
        var result = Resolve(Register(BuildConfig()));
        var expected = new DatabaseOptions();

        result.AssureIndex.Should().Be(expected.AssureIndex);
        result.AssureIndexAtStartup.Should().Be(expected.AssureIndexAtStartup);
        result.AllowDelayedCommit.Should().Be(expected.AllowDelayedCommit);
        result.GuidStorageFormat.Should().Be(expected.GuidStorageFormat);
        result.FailedIndexRecheckInterval.Should().Be(expected.FailedIndexRecheckInterval);
        result.Quilt4NetHeartbeatInterval.Should().Be(expected.Quilt4NetHeartbeatInterval);
        result.Monitor.Enabled.Should().Be(expected.Monitor.Enabled);
        result.Monitor.LastCallsToKeep.Should().Be(expected.Monitor.LastCallsToKeep);
        result.Monitor.SlowCallsToKeep.Should().Be(expected.Monitor.SlowCallsToKeep);
        result.Monitor.CallRecordingLevel.Should().Be(expected.Monitor.CallRecordingLevel);
        result.Monitor.StorageMode.Should().Be(expected.Monitor.StorageMode);
        result.Limiter.Enabled.Should().Be(expected.Limiter.Enabled);
    }

    [Fact]
    public void BothCodeAndAppSettings_CodeStillWins_ForAPropertyTheCopyNeverCarried()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "MongoDB:Monitor:SourceName", "from-appsettings" },
        });

        var result = Resolve(Register(config, o => o.Monitor.SourceName = "from-code")).Monitor;

        result.SourceName.Should().Be("from-code");
    }

    /// <summary>
    /// Bind leaves delegate-typed properties alone, so callbacks a consumer assigns in code survive it.
    /// </summary>
    [Fact]
    public void DelegateProperties_SurviveBinding()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "MongoDB:Monitor:SourceName", "agent-1" },
        });

        var result = Resolve(Register(config, o =>
        {
            o.ConnectionStringLoader = (_, _) => null;
            o.MaxPoolSizeOverride = (_, _, size) => Task.FromResult(size);
        }));

        result.ConnectionStringLoader.Should().NotBeNull();
        result.MaxPoolSizeOverride.Should().NotBeNull();
        result.Monitor.SourceName.Should().Be("agent-1");
    }
}

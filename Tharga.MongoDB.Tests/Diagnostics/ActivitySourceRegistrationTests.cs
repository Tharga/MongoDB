using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests.Diagnostics;

public class ActivitySourceRegistrationTests
{
    private static IServiceProvider Register(Action<DatabaseOptions> options = null, Dictionary<string, string> configuration = null)
    {
        var builder = new ConfigurationBuilder();
        if (configuration != null) builder.AddInMemoryCollection(configuration);

        var services = new ServiceCollection().AddLogging();
        services.AddMongoDB(builder.Build(), o =>
        {
            o.AutoRegisterRepositories = false;
            o.AutoRegisterCollections = false;
            options?.Invoke(o);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ByDefault_TheActivityEmitterIsRegistered()
    {
        var provider = Register();

        provider.GetService<CommandActivityEmitter>().Should().NotBeNull();
    }

    [Fact]
    public void WhenTurnedOff_NoEmitterIsRegisteredAtAll()
    {
        var provider = Register(o => o.Monitor.EnableActivitySource = false);

        provider.GetService<CommandActivityEmitter>().Should().BeNull();
    }

    [Fact]
    public void TheSwitchCanComeFromConfiguration()
    {
        var provider = Register(configuration: new Dictionary<string, string> { { "MongoDB:Monitor:EnableActivitySource", "false" } });

        provider.GetService<CommandActivityEmitter>().Should().BeNull();
    }

    [Fact]
    public void EnableActivitySource_DefaultsToOn()
    {
        new MonitorOptions().EnableActivitySource.Should().BeTrue();
    }

    [Fact]
    public void CaptureCommandText_DefaultsToOff()
    {
        new MonitorOptions().CaptureCommandText.Should().BeFalse();
    }
}

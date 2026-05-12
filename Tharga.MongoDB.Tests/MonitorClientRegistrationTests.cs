using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tharga.Communication.Client;
using Tharga.MongoDB.Monitor.Client;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class MonitorClientRegistrationTests
{
    private static IConfiguration EmptyConfiguration()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>()).Build();

    [Fact]
    public void OnServices_RegistersMonitorForwarder()
    {
        var services = new ServiceCollection();

        services.AddMongoDbMonitorClient(EmptyConfiguration(), sendTo: "https://hub.example/monitor");

        services.Should().Contain(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(MonitorForwarder));
    }

    [Fact]
    public void OnServices_RegistersConfigurationIfMissing()
    {
        var services = new ServiceCollection();
        var config = EmptyConfiguration();

        services.AddMongoDbMonitorClient(config, sendTo: "https://hub.example/monitor");

        var registered = services.FirstOrDefault(d => d.ServiceType == typeof(IConfiguration));
        registered.Should().NotBeNull("IConfiguration should be registered so Tharga.Communication can resolve it");
        registered!.ImplementationInstance.Should().BeSameAs(config);
    }

    [Fact]
    public void OnServices_DoesNotOverrideExistingConfiguration()
    {
        var services = new ServiceCollection();
        var existing = EmptyConfiguration();
        services.AddSingleton<IConfiguration>(existing);

        var fresh = EmptyConfiguration();
        services.AddMongoDbMonitorClient(fresh, sendTo: "https://hub.example/monitor");

        var registered = services.First(d => d.ServiceType == typeof(IConfiguration));
        registered.ImplementationInstance.Should().BeSameAs(existing, "the existing IConfiguration registration must not be replaced");
    }

    [Fact]
    public void OnServices_SkipsWhenSendToIsEmpty()
    {
        var services = new ServiceCollection();

        services.AddMongoDbMonitorClient(EmptyConfiguration(), sendTo: null);

        services.Should().NotContain(d => d.ImplementationType == typeof(MonitorForwarder),
            "with no sendTo the registration is a no-op, matching the existing builder overload");
    }

    [Fact]
    public void OnServices_PassesSendToAndApiKeyToCommunicationOptions()
    {
        var services = new ServiceCollection();

        services.AddMongoDbMonitorClient(
            EmptyConfiguration(),
            sendTo: "https://hub.example/monitor",
            apiKey: "secret-key");

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<Tharga.Communication.Client.CommunicationOptions>>().Value;
        options.ServerAddress.Should().Be("https://hub.example/monitor");
        options.ApiKey.Should().Be("secret-key");
        options.Pattern.Should().Be(MonitorConstants.DefaultHubPattern);
    }
}

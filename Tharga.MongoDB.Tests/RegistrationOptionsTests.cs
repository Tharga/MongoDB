using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Tharga.MongoDB.Configuration;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class RegistrationOptionsTests
{
    private static IServiceCollection CreateServices() => new ServiceCollection().AddLogging();

    private static IConfiguration BuildConfig(Dictionary<string, string> values = null)
    {
        var builder = new ConfigurationBuilder();
        if (values != null)
            builder.AddInMemoryCollection(values);
        return builder.Build();
    }

    private static IServiceProvider Register(IConfiguration config, Action<DatabaseOptions> options = null)
    {
        var services = CreateServices();
        services.AddMongoDB(config, o =>
        {
            o.AutoRegisterRepositories = false;
            o.AutoRegisterCollections = false;
            options?.Invoke(o);
        });
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// BsonSerializer has global static state: registering a different GuidSerializer after the first
    /// call throws BsonSerializationException. Since Options.Create(o) is registered before that call,
    /// we catch the exception so we can still verify the DatabaseOptions value.
    /// </summary>
    private static IServiceProvider RegisterForGuidRepresentation(IConfiguration config, Action<DatabaseOptions> options = null)
    {
        var services = CreateServices();
        try
        {
            services.AddMongoDB(config, o =>
            {
                o.AutoRegisterRepositories = false;
                o.AutoRegisterCollections = false;
                options?.Invoke(o);
            });
        }
        catch (global::MongoDB.Bson.BsonSerializationException)
        {
            // Global serializer conflict is a test-isolation issue, not a test failure.
            // IOptions<DatabaseOptions> is registered before BsonSerializer is called, so we can proceed.
        }
        return services.BuildServiceProvider();
    }

    // --- Limiter ---

    [Fact]
    public void Limiter_NoConfiguration_EnabledIsTrue()
    {
        var provider = Register(BuildConfig());

        var result = provider.GetRequiredService<IOptions<ExecuteLimiterOptions>>().Value;

        result.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Limiter_DisabledViaCode_EnabledIsFalse()
    {
        var provider = Register(BuildConfig(), o => o.Limiter.Enabled = false);

        var result = provider.GetRequiredService<IOptions<ExecuteLimiterOptions>>().Value;

        result.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Limiter_DisabledViaAppSettings_EnabledIsFalse()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "MongoDB:Limiter:Enabled", "false" }
        });

        var provider = Register(config);

        var result = provider.GetRequiredService<IOptions<ExecuteLimiterOptions>>().Value;

        result.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Limiter_NoConfiguration_DefaultsTo20()
    {
        var provider = Register(BuildConfig());

        var result = provider.GetRequiredService<IOptions<ExecuteLimiterOptions>>().Value;

        result.MaxConcurrent.Should().Be(20);
    }

    [Fact]
    public void Limiter_CodeConfigured_UsesCodeValue()
    {
        var provider = Register(BuildConfig(), o => o.Limiter.MaxConcurrent = 5);

        var result = provider.GetRequiredService<IOptions<ExecuteLimiterOptions>>().Value;

        result.MaxConcurrent.Should().Be(5);
    }

    [Fact]
    public void Limiter_AppSettingsConfigured_UsesAppSettingsValue()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "MongoDB:Limiter:MaxConcurrent", "10" }
        });

        var provider = Register(config);

        var result = provider.GetRequiredService<IOptions<ExecuteLimiterOptions>>().Value;

        result.MaxConcurrent.Should().Be(10);
    }

    [Fact]
    public void Limiter_BothCodeAndAppSettings_CodeWins()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "MongoDB:Limiter:MaxConcurrent", "10" }
        });

        var provider = Register(config, o => o.Limiter.MaxConcurrent = 5);

        var result = provider.GetRequiredService<IOptions<ExecuteLimiterOptions>>().Value;

        result.MaxConcurrent.Should().Be(5);
    }

    // --- Monitor ---

    [Fact]
    public void Monitor_NoConfiguration_DefaultsApplied()
    {
        var provider = Register(BuildConfig());

        var result = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Monitor;

        result.Enabled.Should().BeTrue();
        result.LastCallsToKeep.Should().Be(1000);
        result.SlowCallsToKeep.Should().Be(200);
    }

    [Fact]
    public void Monitor_CodeConfigured_UsesCodeValue()
    {
        var provider = Register(BuildConfig(), o => o.Monitor.SlowCallsToKeep = 1);

        var result = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Monitor;

        result.SlowCallsToKeep.Should().Be(1);
    }

    [Fact]
    public void Monitor_AppSettingsConfigured_UsesAppSettingsValue()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "MongoDB:Monitor:SlowCallsToKeep", "50" },
            { "MongoDB:Monitor:LastCallsToKeep", "500" }
        });

        var provider = Register(config);

        var result = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Monitor;

        result.SlowCallsToKeep.Should().Be(50);
        result.LastCallsToKeep.Should().Be(500);
    }

    [Fact]
    public void Monitor_BothCodeAndAppSettings_CodeWins()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "MongoDB:Monitor:SlowCallsToKeep", "50" }
        });

        var provider = Register(config, o => o.Monitor.SlowCallsToKeep = 1);

        var result = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Monitor;

        result.SlowCallsToKeep.Should().Be(1);
    }

    // --- GuidRepresentation ---

    [Fact]
    public void GuidRepresentation_NoConfiguration_IsNull()
    {
        var provider = RegisterForGuidRepresentation(BuildConfig());

        var result = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.GuidRepresentation;

        result.Should().BeNull();
    }

    [Fact]
    public void GuidRepresentation_CodeConfigured_UsesCodeValue()
    {
        var provider = RegisterForGuidRepresentation(BuildConfig(), o => o.GuidRepresentation = GuidRepresentation.Standard);

        var result = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.GuidRepresentation;

        result.Should().Be(GuidRepresentation.Standard);
    }

    [Fact]
    public void GuidRepresentation_AppSettingsConfigured_UsesAppSettingsValue()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "MongoDB:GuidRepresentation", "Standard" }
        });

        var provider = RegisterForGuidRepresentation(config);

        var result = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.GuidRepresentation;

        result.Should().Be(GuidRepresentation.Standard);
    }

    [Fact]
    public void GuidRepresentation_BothCodeAndAppSettings_CodeWins()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "MongoDB:GuidRepresentation", "Standard" }
        });

        var provider = RegisterForGuidRepresentation(config, o => o.GuidRepresentation = GuidRepresentation.JavaLegacy);

        var result = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.GuidRepresentation;

        result.Should().Be(GuidRepresentation.JavaLegacy);
    }
}

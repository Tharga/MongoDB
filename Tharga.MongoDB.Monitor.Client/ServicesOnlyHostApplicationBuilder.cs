using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tharga.MongoDB.Monitor.Client;

/// <summary>
/// Minimal <see cref="IHostApplicationBuilder"/> adapter used by the
/// <see cref="MonitorClientRegistration.AddMongoDbMonitorClient(IServiceCollection, IConfiguration, string, string)"/>
/// overload to delegate to the existing builder-based overload without duplicating
/// any of its body. Exposes only <see cref="Services"/>, <see cref="Properties"/> and
/// <see cref="Configuration"/> (a read-only view of the supplied configuration) because that is what
/// <c>AddMongoDbMonitorClient</c> and Tharga.Communication's <c>AddThargaCommunicationClient</c> (1.3.0) actually use. Other members throw
/// <see cref="NotSupportedException"/> so future upstream changes that start using
/// them fail loudly and visibly rather than silently misbehaving.
/// </summary>
internal sealed class ServicesOnlyHostApplicationBuilder : IHostApplicationBuilder
{
    private readonly ConfigurationManager _configuration = new();

    public ServicesOnlyHostApplicationBuilder(IServiceCollection services, IConfiguration configuration = null)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Properties = new Dictionary<object, object>();
        if (configuration != null) _configuration.AddConfiguration(configuration);
    }

    public IServiceCollection Services { get; }

    public IDictionary<object, object> Properties { get; }

    private const string NotSupportedMessage =
        "ServicesOnlyHostApplicationBuilder is a services-only adapter used internally by AddMongoDbMonitorClient(IServiceCollection, ...). "
        + "Only Services, Properties and Configuration are supported.";

    public IConfigurationManager Configuration => _configuration;

    public IHostEnvironment Environment => throw new NotSupportedException(NotSupportedMessage);

    public ILoggingBuilder Logging => throw new NotSupportedException(NotSupportedMessage);

    public IMetricsBuilder Metrics => throw new NotSupportedException(NotSupportedMessage);

    public void ConfigureContainer<TContainerBuilder>(IServiceProviderFactory<TContainerBuilder> factory, Action<TContainerBuilder> configure = null)
        where TContainerBuilder : notnull
        => throw new NotSupportedException(NotSupportedMessage);
}

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
/// any of its body. Exposes only <see cref="Services"/> and <see cref="Properties"/>
/// because that is what <c>AddMongoDbMonitorClient</c> and Tharga.Communication's
/// <c>AddThargaCommunicationClient</c> (0.1.5) actually use. Other members throw
/// <see cref="NotSupportedException"/> so future upstream changes that start using
/// them fail loudly and visibly rather than silently misbehaving.
/// </summary>
internal sealed class ServicesOnlyHostApplicationBuilder : IHostApplicationBuilder
{
    public ServicesOnlyHostApplicationBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Properties = new Dictionary<object, object>();
    }

    public IServiceCollection Services { get; }

    public IDictionary<object, object> Properties { get; }

    private const string NotSupportedMessage =
        "ServicesOnlyHostApplicationBuilder is a services-only adapter used internally by AddMongoDbMonitorClient(IServiceCollection, ...). "
        + "Only Services and Properties are supported.";

    public IConfigurationManager Configuration => throw new NotSupportedException(NotSupportedMessage);

    public IHostEnvironment Environment => throw new NotSupportedException(NotSupportedMessage);

    public ILoggingBuilder Logging => throw new NotSupportedException(NotSupportedMessage);

    public IMetricsBuilder Metrics => throw new NotSupportedException(NotSupportedMessage);

    public void ConfigureContainer<TContainerBuilder>(IServiceProviderFactory<TContainerBuilder> factory, Action<TContainerBuilder> configure = null)
        where TContainerBuilder : notnull
        => throw new NotSupportedException(NotSupportedMessage);
}

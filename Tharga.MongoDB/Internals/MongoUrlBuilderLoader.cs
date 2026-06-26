using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Internals;

internal class MongoUrlBuilderLoader : IMongoUrlBuilderLoader
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DatabaseOptions _databaseOptions;

    public MongoUrlBuilderLoader(IServiceProvider serviceProvider, DatabaseOptions databaseOptions)
    {
        _serviceProvider = serviceProvider;
        _databaseOptions = databaseOptions;
    }

    public (IMongoUrlBuilder Builder, Func<string> ConnectionStringLoader, Func<MongoUrl, MongoUrl> ApplyPoolSizeOverride) GetConnectionStringBuilder(DatabaseContext databaseContext)
    {
        var builder = new Lazy<IMongoUrlBuilder>(() =>
        {
            var hostEnvironment = (IHostEnvironment)_serviceProvider.GetService(typeof(IHostEnvironment));
            return new MongoUrlBuilder(hostEnvironment);
        });

        return (
            (IMongoUrlBuilder)_serviceProvider.GetService(typeof(IMongoUrlBuilder)) ?? builder.Value,
            () => GetConnectionString(databaseContext, _databaseOptions, _serviceProvider),
            url => ApplyMaxPoolSizeOverride(url, databaseContext));
    }

    // Applies DatabaseOptions.MaxPoolSizeOverride to the built URL so the overridden MaxPoolSize feeds both
    // the MongoClient settings and the client cache key (MongoDbClientProvider.GetServerKey). Runs once per
    // URL construction (not on the hot path); sync-over-async mirrors the ConnectionStringLoader call above.
    private MongoUrl ApplyMaxPoolSizeOverride(MongoUrl url, DatabaseContext databaseContext)
    {
        var ovr = _databaseOptions.MaxPoolSizeOverride;
        if (ovr == null || url == null) return url;

        var configurationName = databaseContext?.ConfigurationName?.Value.NullIfEmpty() ?? _databaseOptions.DefaultConfigurationName;
        if (configurationName == null) return url;

        var current = url.MaxConnectionPoolSize;
        var resolved = ovr(_serviceProvider, configurationName, current).GetAwaiter().GetResult();
        if (resolved == current) return url;

        return new global::MongoDB.Driver.MongoUrlBuilder(url.ToString()) { MaxConnectionPoolSize = resolved }.ToMongoUrl();
    }

    private string GetConnectionString(DatabaseContext databaseContext, DatabaseOptions databaseOptions, IServiceProvider provider)
    {
        var configurationName = databaseContext?.ConfigurationName?.Value.NullIfEmpty() ?? databaseOptions.DefaultConfigurationName;

        if (configurationName == null) throw new InvalidOperationException("Cannot find configuration name.");

        var providedConnectionString = databaseOptions.ConnectionStringLoader?.Invoke(configurationName, _serviceProvider)?.GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(providedConnectionString?.Value))
        {
            return providedConnectionString.Value;
        }

        var configuration = (IConfiguration)provider.GetService(typeof(IConfiguration)) ?? throw new NullReferenceException("Cannot get instance of IConfiguration.");
        var connectionString = configuration.GetConnectionString(configurationName);
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException($"Cannot find 'ConnectionStrings/{configurationName}' in configuration and '{nameof(DatabaseOptions.ConnectionStringLoader)}(\"{configurationName}\")' was not provided in {nameof(DatabaseOptions)}.");
        }

        return connectionString;
    }
}
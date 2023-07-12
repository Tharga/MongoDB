using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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

    public (IMongoUrlBuilder Builder, Func<string> ConnectionStringLoader) GetConnectionStringBuilder(DatabaseContext databaseContext)
    {
        var builder = new Lazy<IMongoUrlBuilder>(() =>
        {
            var hostEnvironment = (IHostEnvironment)_serviceProvider.GetService(typeof(IHostEnvironment));
            return new MongoUrlBuilder(hostEnvironment);
        });

        return ((IMongoUrlBuilder)_serviceProvider.GetService(typeof(IMongoUrlBuilder)) ?? builder.Value, () => GetConnectionString(databaseContext, _databaseOptions, _serviceProvider));
    }

    private static string GetConnectionString(DatabaseContext databaseContext, DatabaseOptions databaseOptions, IServiceProvider provider)
    {
        var configurationName = databaseContext?.ConfigurationName?.Value.NullIfEmpty() ?? databaseOptions.ConfigurationName.Value;

        var providedConnectionString = databaseOptions.ConnectionStringLoader?.Invoke(configurationName);
        if (!string.IsNullOrEmpty(providedConnectionString?.Value))
        {
            return providedConnectionString?.Value;
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
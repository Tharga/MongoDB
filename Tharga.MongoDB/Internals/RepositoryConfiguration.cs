﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Internals;

internal class RepositoryConfiguration : IRepositoryConfiguration
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly DatabaseOptions _databaseOptions;

    internal RepositoryConfiguration(IServiceProvider serviceProvider, DatabaseOptions databaseOptions)
    {
        _serviceProvider = serviceProvider;
        _configuration = serviceProvider.GetService<IConfiguration>();
        _databaseOptions = databaseOptions;
    }

    public string GetRawDatabaseUrl(string configurationName = null)
    {
        var name = configurationName ?? _databaseOptions.ConfigurationName ?? "Default";
        return _databaseOptions.ConnectionStringLoader?.Invoke(name, _serviceProvider).GetAwaiter().GetResult() ?? _configuration.GetConnectionString(name);
    }

    public MongoDbConfig GetConfiguration(string configurationName = null)
    {
        var name = configurationName ?? _databaseOptions.ConfigurationName ?? "Default";

        //Provided as named parameter
        MongoDbConfiguration c1 = null;
        var configurationTree = _databaseOptions.ConfigurationLoader?.Invoke(_serviceProvider)?.GetAwaiter().GetResult();
        configurationTree?.Configurations?.TryGetValue(name, out c1);

        //Provided as general parameter
        var c2 = configurationTree as MongoDbConfiguration;

        //Configured as named parameter
        var c3 = new Lazy<MongoDbConfiguration>(() => GetConfigValue<MongoDbConfiguration>($"MongoDB:{name}"));

        //Configured as general parameter
        var c4 = new Lazy<MongoDbConfiguration>(() => GetConfigValue<MongoDbConfiguration>("MongoDB"));

        var configuration = new MongoDbConfig
        {
            AccessInfo = c1?.AccessInfo ?? c2?.AccessInfo ?? c3.Value?.AccessInfo ?? c4.Value?.AccessInfo,
            ResultLimit = c1?.ResultLimit ?? c2?.ResultLimit ?? c3.Value?.ResultLimit ?? c4.Value?.ResultLimit,
            AutoClean = c1?.AutoClean ?? c2?.AutoClean ?? c3.Value?.AutoClean ?? c4.Value?.AutoClean ?? true,
            CleanOnStartup = c1?.CleanOnStartup ?? c2?.CleanOnStartup ?? c3.Value?.CleanOnStartup ?? c4.Value?.CleanOnStartup ?? false,
            DropEmptyCollections = c1?.DropEmptyCollections ?? c2?.DropEmptyCollections ?? c3.Value?.DropEmptyCollections ?? c4.Value?.DropEmptyCollections ?? true
        };

        return configuration;
    }

    public IEnumerable<string> GetDatabaseConfigurationNames()
    {
        foreach (var p in GetAllConfigurationNames().Distinct())
        {
            yield return p;
        }
    }

    private IEnumerable<string> GetAllConfigurationNames()
    {
        var any = false;
        var connectionStrings = _configuration.GetSection("ConnectionStrings");
        foreach (var connectionString in connectionStrings.GetChildren())
        {
            any = true;
            yield return connectionString.Key;
        }

        if (!any || !string.IsNullOrEmpty(_databaseOptions.ConfigurationName))
        {
            yield return _databaseOptions.ConfigurationName ?? "Default";
        }
    }

    private T GetConfigValue<T>(string path) where T : new()
    {
        var obj = new T();
        _configuration?.GetSection(path).Bind(obj);
        return obj;
    }
}
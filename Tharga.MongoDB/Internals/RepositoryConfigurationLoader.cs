using System;
using Microsoft.Extensions.Configuration;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Internals;

internal class RepositoryConfigurationLoader : IRepositoryConfigurationLoader
{
    private readonly IConfiguration _configuration;
    private readonly IMongoUrlBuilderLoader _mongoUrlBuilderLoader;
    private readonly DatabaseOptions _databaseOptions;

    public RepositoryConfigurationLoader(IConfiguration configuration, IMongoUrlBuilderLoader mongoUrlBuilderLoader, DatabaseOptions databaseOptions)
    {
        _configuration = configuration;
        _mongoUrlBuilderLoader = mongoUrlBuilderLoader;
        _databaseOptions = databaseOptions;
    }

    public IRepositoryConfiguration GetConfiguration(Func<DatabaseContext> databaseContextLoader)
    {
        return new RepositoryConfiguration(_configuration, _mongoUrlBuilderLoader, _databaseOptions, databaseContextLoader);
    }
}
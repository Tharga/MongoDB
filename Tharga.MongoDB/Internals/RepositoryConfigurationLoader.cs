using System;
using Microsoft.Extensions.Configuration;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Internals;

internal class RepositoryConfigurationLoader : IRepositoryConfigurationLoader
{
    private readonly IConfiguration _configuration;
    private readonly IMongoUrlBuilderLoader _mongoUrlBuilderLoader;
    private readonly MongoDbConfigurationTree _mongoDbConfiguration;

    public RepositoryConfigurationLoader(IConfiguration configuration, IMongoUrlBuilderLoader mongoUrlBuilderLoader, MongoDbConfigurationTree mongoDbConfiguration)
    {
        _configuration = configuration;
        _mongoUrlBuilderLoader = mongoUrlBuilderLoader;
        _mongoDbConfiguration = mongoDbConfiguration;
    }

    public IRepositoryConfiguration GetConfiguration(Func<DatabaseContext> databaseContextLoader)
    {
        return new RepositoryConfiguration(_configuration, _mongoUrlBuilderLoader, _mongoDbConfiguration, databaseContextLoader);
    }
}
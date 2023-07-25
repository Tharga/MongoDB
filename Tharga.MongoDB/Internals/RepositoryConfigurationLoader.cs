using System;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Internals;

internal class RepositoryConfigurationLoader : IRepositoryConfigurationLoader
{
    private readonly IMongoUrlBuilderLoader _mongoUrlBuilderLoader;
    private readonly IRepositoryConfiguration _repositoryConfiguration;
    private readonly DatabaseOptions _databaseOptions;

    public RepositoryConfigurationLoader(IMongoUrlBuilderLoader mongoUrlBuilderLoader, IRepositoryConfiguration repositoryConfiguration, DatabaseOptions databaseOptions)
    {
        _mongoUrlBuilderLoader = mongoUrlBuilderLoader;
        _repositoryConfiguration = repositoryConfiguration;
        _databaseOptions = databaseOptions;
    }

    public IRepositoryConfigurationInternal GetConfiguration(Func<DatabaseContext> databaseContextLoader)
    {
        return new RepositoryConfigurationInternal(_mongoUrlBuilderLoader, _repositoryConfiguration, _databaseOptions, databaseContextLoader);
    }
}
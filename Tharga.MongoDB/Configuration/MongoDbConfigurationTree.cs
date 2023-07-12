using System.Collections.Generic;

namespace Tharga.MongoDB.Configuration;

public record MongoDbConfigurationTree : MongoDbConfiguration
{
    public Dictionary<ConfigurationName, MongoDbConfiguration> Configurations { get; init; }
}
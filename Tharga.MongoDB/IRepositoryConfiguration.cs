﻿using System.Collections.Generic;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

public interface IRepositoryConfiguration
{
    string GetRawDatabaseUrl(string configurationName = null);
    MongoDbConfig GetConfiguration(string configurationName = null);
    IEnumerable<string> GetDatabaseConfigurationNames();
}
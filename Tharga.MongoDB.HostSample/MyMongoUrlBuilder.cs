using System;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;

namespace Tharga.MongoDB.HostSample;

internal class MyMongoUrlBuilder : IMongoUrlBuilder
{
    private readonly IHostEnvironment _hostEnvironment;

    public MyMongoUrlBuilder(IHostEnvironment hostEnvironment)
    {
        _hostEnvironment = hostEnvironment;
    }

    public MongoUrl Build(string connectionString, string databasePart)
    {
        var environmentReplacement = GetEnvironmentReplacement();
        var partReplacement = GetPartEnvironment(databasePart);

        var url = connectionString
            .Replace("{environment}", environmentReplacement)
            .Replace("{part}", partReplacement);

        if (url.Contains("{")) throw new InvalidOperationException($"ConnectionString '{url}' still contains variables.");

        var mongoUrl = new MongoUrl(url);
        return mongoUrl;
    }

    private static string GetPartEnvironment(string part)
    {
        var partString = !string.IsNullOrEmpty(part) ? $"_{part}" : "";
        return partString;
    }

    private string GetEnvironmentReplacement()
    {
        var environment = _hostEnvironment?.EnvironmentName;
        var environmentString = string.IsNullOrEmpty(environment) || environment == "Production" ? string.Empty : $"_{environment}";
        return environmentString;
    }
}
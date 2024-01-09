using Microsoft.Extensions.Logging;

namespace Tharga.MongoDB.Configuration;

public record UseMongoOptions
{
    public ILogger Logger { get; set; }
    public DatabaseUsage DatabaseUsage { get; set; }
    public bool WaitToComplete { get; set; }
}
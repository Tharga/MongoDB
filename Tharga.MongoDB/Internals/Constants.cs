namespace Tharga.MongoDB.Internals;

internal static class Constants
{
    public const string DefaultConfigurationName = "Default";
    public const bool AutoRegisterRepositoriesDefault = true;
    public const bool AutoRegisterCollectionsDefault = true;
    public const string GetCollectionObsoleteMessage = "Use ExecuteAsync instead. GetCollection blocks the calling thread until the collection has been fetched and initiated, and will be deprecated.";
}
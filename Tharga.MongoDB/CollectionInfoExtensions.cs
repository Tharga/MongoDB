namespace Tharga.MongoDB;

public static class CollectionInfoExtensions
{
    public static DatabaseContext ToDatabaseContext(this CollectionInfo item)
    {
        return new DatabaseContext
        {
            ConfigurationName = item.ConfigurationName,
            CollectionName = item.CollectionName,
            DatabasePart = item.DatabasePart
        };
    }
}
namespace Tharga.MongoDB.Configuration;

public static class MongoDbApiAccessExtensions
{
    public static bool HasMongoDbApiAccess(this MongoDbApiAccess item)
    {
        if (item == null) return false;
        if (string.IsNullOrEmpty(item.PublicKey)) return false;
        if (string.IsNullOrEmpty(item.PrivateKey)) return false;
        if (string.IsNullOrEmpty(item.GroupId)) return false;
        return true;
    }
}
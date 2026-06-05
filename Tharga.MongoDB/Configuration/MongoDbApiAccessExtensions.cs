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

    internal static FirewallMode GetFirewallMode(this MongoDbApiAccess item)
    {
        if (item == null) return FirewallMode.None;

        var hasAtlas = !string.IsNullOrEmpty(item.PublicKey)
                       && !string.IsNullOrEmpty(item.PrivateKey)
                       && !string.IsNullOrEmpty(item.GroupId);
        var hasQuilt4Net = !string.IsNullOrEmpty(item.Quilt4NetApiKey)
                           && !string.IsNullOrEmpty(item.GroupId);

        if (hasAtlas && hasQuilt4Net) return FirewallMode.Notify;
        if (hasQuilt4Net) return FirewallMode.Open;
        if (hasAtlas) return FirewallMode.Classic;
        return FirewallMode.None;
    }
}
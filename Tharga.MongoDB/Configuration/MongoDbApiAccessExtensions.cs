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

    // True when any firewall mode is configured (Classic, Notify or Open). Unlike HasMongoDbApiAccess this also covers Quilt4Net-only (Open) configs, so the startup firewall open is attempted for them too.
    internal static bool HasFirewallConfiguration(this MongoDbApiAccess item)
    {
        return item.GetFirewallMode() != FirewallMode.None;
    }
}
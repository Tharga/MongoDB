namespace Tharga.MongoDB.Configuration;

/// <summary>
/// Inferred firewall coordination mode for a <see cref="MongoDbApiAccess"/>.
/// The consumer never sets this directly — it is computed from which keys are
/// populated via <see cref="MongoDbApiAccessExtensions.GetFirewallMode"/>.
/// </summary>
internal enum FirewallMode
{
    /// <summary>No firewall information supplied — nothing to do.</summary>
    None,

    /// <summary>Direct Atlas firewall open using <c>PublicKey</c>/<c>PrivateKey</c>. Today's behaviour.</summary>
    Classic,

    /// <summary>Direct Atlas firewall open + periodic Quilt4Net heartbeat (<c>ReportUsedAsync</c>) so Quilt4Net knows this IP is in use.</summary>
    Notify,

    /// <summary>Quilt4Net opens the firewall via its proxy (<c>OpenAsync</c>). Subsequent heartbeats also use <c>OpenAsync</c> — when the firewall is already open the call simply registers usage. No direct Atlas API call.</summary>
    Open,
}

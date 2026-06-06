namespace Tharga.MongoDB.Configuration;

public record MongoDbApiAccess
{
    /// <summary>
    /// Organization Access Manager - API Key (Public Key)
    /// </summary>
    public string PublicKey { get; init; }

    /// <summary>
    /// Organization Access Manager - API Key (Private Key)
    /// </summary>
    public string PrivateKey { get; init; }

    /// <summary>
    /// Value of the GroupId in Atlas MongoDB.
    /// </summary>
    public string GroupId { get; init; }

    /// <summary>
    /// A name to be added as a comment for the firewall rule.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Quilt4Net server base address used by the Atlas firewall proxy client.
    /// When not provided, defaults to Quilt4Net.Toolkit's own default (<c>https://quilt4net.com/</c>).
    /// Only consulted when <see cref="Quilt4NetApiKey"/> is also provided.
    /// </summary>
    public string Quilt4NetBaseUrl { get; init; }

    /// <summary>
    /// Quilt4Net firewall API key for this Atlas project. When set together with the Atlas
    /// keys (<see cref="PublicKey"/> + <see cref="PrivateKey"/>) the consumer opens the firewall
    /// directly via Atlas AND heartbeats Quilt4Net (Notify mode). When set without the Atlas
    /// keys, Quilt4Net opens the firewall via its proxy (Open mode — needs a <c>firewall:manage</c>
    /// scope key). When omitted, no Quilt4Net coordination occurs.
    /// </summary>
    public string Quilt4NetApiKey { get; init; }
}
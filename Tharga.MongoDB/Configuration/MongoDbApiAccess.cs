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
}
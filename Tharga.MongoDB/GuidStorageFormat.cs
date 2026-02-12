namespace Tharga.MongoDB;

/// <summary>
/// Controls how Guid values are stored in MongoDB.
/// </summary>
public enum GuidStorageFormat
{
    /// <summary>
    /// RFC 4122 binary UUID (BSON subtype 4). Cross-driver and cross-language compatible.
    /// Recommended for all new projects.
    /// </summary>
    Standard,

    /// <summary>
    /// Legacy C# driver binary format (BSON subtype 3, mixed-endian byte order).
    /// Use only when reading or writing existing data stored by the old C# MongoDB driver.
    /// </summary>
    CSharpLegacy,

    /// <summary>
    /// Human-readable string format: "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx".
    /// Useful when documents must be readable in Compass or bridged to string-based systems.
    /// </summary>
    String
}

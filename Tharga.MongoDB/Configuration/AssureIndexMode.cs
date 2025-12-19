namespace Tharga.MongoDB.Configuration;

public enum AssureIndexMode
{
    /// <summary>
    /// Use the index name to verify that the defined index matches the actual index.
    /// This is a fast method, but be careful. If an index is changed, you also have to change the name, or the old index will not be changed.
    /// </summary>
    ByName,

    /// <summary>
    /// This method is slower but safer. It compares the defined index with the actual index based on the complete schema.
    /// </summary>
    BySchema,

    /// <summary>
    /// This is a crude and simple method. All indexes are dropped and recreated.
    /// </summary>
    DropCreate,

    /// <summary>
    /// Automatic index assurance is disabled.
    /// </summary>
    Disabled,
}
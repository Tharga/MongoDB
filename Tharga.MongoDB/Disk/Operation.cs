namespace Tharga.MongoDB.Disk;

public enum Operation
{
    /// <summary>
    /// Create operations might initiate indices, .
    /// </summary>
    Create,

    /// <summary>
    /// Read operations will not change the collection.
    /// </summary>
    Read,

    /// <summary>
    /// Use for 'CreateOrUpdate' features.
    /// </summary>
    Update,

    /// <summary>
    /// Delete operations.
    /// </summary>
    Delete,
}
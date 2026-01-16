namespace Tharga.MongoDB;

public enum EMode
{
    /// <summary>
    /// Not thread safe. Will first find an Id and then update it. Changes could happen in between.
    /// </summary>
    SingleOrDefault,

    /// <summary>
    /// Not thread safe. Will first find an Id and then update it. Changes could happen in between.
    /// </summary>
    Single,

    /// <summary>
    /// Atomic operation that is safe over several instances.
    /// </summary>
    FirstOrDefault,

    /// <summary>
    /// Not thread safe. Will first find an Id and then update it. Changes could happen in between.
    /// </summary>
    First,
}
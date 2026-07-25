using System;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB.Interception;

/// <summary>
/// Describes a single repository operation to an <see cref="ICollectionInterceptor"/>, before the
/// operation runs.
/// <para>
/// Constructed by the package only when at least one interceptor is registered, so consumers with
/// no interceptors pay nothing for it.
/// </para>
/// </summary>
public sealed record CollectionCallInfo
{
    /// <summary>
    /// The resolved collection name, after any <see cref="DatabaseContext"/> override.
    /// </summary>
    public required string CollectionName { get; init; }

    /// <summary>
    /// The name of the repository method being called, for example <c>GetOneAsync</c> or
    /// <c>UpdateManyAsync</c>.
    /// <para>
    /// Operations on a lockable collection report the underlying disk operation that actually runs —
    /// a <c>PickForUpdateAsync</c> reports as <c>UpdateOneAsync</c>. Match on
    /// <see cref="OperationType"/> rather than this string when the intent is to classify reads
    /// against writes.
    /// </para>
    /// </summary>
    public required string Operation { get; init; }

    /// <summary>
    /// The operation's read/write classification, as the package itself classifies it for index
    /// assurance. Use this to distinguish a read from a mutation.
    /// </summary>
    public required Operation OperationType { get; init; }

    /// <summary>The entity type the collection stores.</summary>
    public required Type EntityType { get; init; }

    /// <summary>
    /// The point in the operation's lifetime that this invocation represents. An interceptor that
    /// declared both points is called twice for a deferred operation and can tell the calls apart
    /// by this value.
    /// </summary>
    public required InterceptionPoint Point { get; init; }

    /// <summary>
    /// The configuration name the collection resolves against, or the default configuration name
    /// when the collection does not specify one.
    /// </summary>
    public string ConfigurationName { get; init; }

    /// <summary>The resolved database name.</summary>
    public string DatabaseName { get; init; }

    /// <summary>
    /// The database context the collection was built with, or null for a statically registered
    /// collection that takes no context.
    /// </summary>
    public DatabaseContext DatabaseContext { get; init; }
}

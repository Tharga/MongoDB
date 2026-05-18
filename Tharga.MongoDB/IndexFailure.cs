using Tharga.MongoDB.Internals;

namespace Tharga.MongoDB;

/// <summary>
/// A recorded failure to drop or create a specific index on a collection within
/// the current process. Surfaced via
/// <c>IDiskRepositoryCollection&lt;TEntity&gt;.GetFailedIndices()</c> so consumers
/// can build admin UIs without writing collection-specific auditors. Scope is
/// in-process; cross-process persistence is a planned follow-up.
/// </summary>
public sealed record IndexFailure
{
    /// <summary>The operation that failed (Create or Drop).</summary>
    public required IndexFailOperation Operation { get; init; }

    /// <summary>Name of the index whose operation failed.</summary>
    public required string Name { get; init; }

    /// <summary>The most recent error message captured for this (Operation, Name).</summary>
    public required string LastErrorMessage { get; init; }
}

namespace Tharga.MongoDB;

/// <summary>
/// Progress event raised by <see cref="IDatabaseMonitor.RestoreAllIndicesAsync"/> as
/// each collection is processed. One event per collection — used by the Blazor toolbar
/// to show a notification per step and by the MCP tool to stream progress.
/// </summary>
public record IndexAssureProgress
{
    /// <summary>Zero-based index of the current collection in the iteration.</summary>
    public required int Index { get; init; }

    /// <summary>Total number of collections that will be processed.</summary>
    public required int Total { get; init; }

    /// <summary>The collection being processed.</summary>
    public required CollectionInfo CollectionInfo { get; init; }

    /// <summary>True if the call to <c>RestoreIndexAsync</c> succeeded.</summary>
    public required bool Success { get; init; }

    /// <summary>True when the collection was skipped (e.g. <see cref="Registration.NotInCode"/>).</summary>
    public required bool Skipped { get; init; }

    /// <summary>The exception captured if processing failed; null when <see cref="Success"/> is true or skipped.</summary>
    public System.Exception Error { get; init; }
}

/// <summary>
/// Final summary returned by <see cref="IDatabaseMonitor.RestoreAllIndicesAsync"/>.
/// Counts mirror the per-collection outcomes reported via <see cref="IndexAssureProgress"/>.
/// </summary>
public record IndexAssureSummary
{
    public required int Total { get; init; }
    public required int Succeeded { get; init; }
    public required int Failed { get; init; }
    public required int Skipped { get; init; }
}

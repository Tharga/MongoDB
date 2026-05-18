using System.Collections.Concurrent;

namespace Tharga.MongoDB.Internals;

internal record InitiationInfo
{
    public bool IndexAssured { get; set; }
    public long? VirtualCount { get; set; }

    // Keyed by (Operation, IndexName); value is the latest error message from the
    // most recent failure of that operation+name in this process. ConcurrentDictionary
    // makes AddFailedInitiateIndex idempotent on the key and gives O(1) lookup for
    // the "already-known?" check that drives log severity downgrade on retries.
    public ConcurrentDictionary<(IndexFailOperation Operation, string Name), string> FailedIndices { get; set; } = new();
}
using System.Collections.Concurrent;

namespace Tharga.MongoDB.Internals;

internal record InitiationInfo
{
    public bool IndexAssured { get; set; }
    public long? VirtualCount { get; set; }

    // Components of the outer dictionary's key, stored here so the library can enumerate
    // collections-with-failures without having to parse "{server}.{db}.{collection}" back
    // (server/db/collection names may themselves contain dots).
    public string ServerName { get; init; }
    public string DatabaseName { get; init; }
    public string CollectionName { get; init; }

    // Keyed by (Operation, IndexName); value is the latest error message from the
    // most recent failure of that operation+name in this process. ConcurrentDictionary
    // makes AddFailedInitiateIndex idempotent on the key and gives O(1) lookup for
    // the "already-known?" check that drives log severity downgrade on retries.
    public ConcurrentDictionary<(IndexFailOperation Operation, string Name), string> FailedIndices { get; set; } = new();
}
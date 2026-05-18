using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Tharga.MongoDB.Internals;

internal class InitiationLibrary : IInitiationLibrary
{
    private readonly ConcurrentDictionary<string, InitiationInfo> _initiated = new();

    public bool ShouldInitiate(string serverName, string databaseName, string collectionName)
    {
        return _initiated.TryAdd($"{serverName}.{databaseName}.{collectionName}", new InitiationInfo { IndexAssured = false });
    }

    public bool ShouldInitiateIndex(string serverName, string databaseName, string collectionName)
    {
        if (!_initiated.TryGetValue($"{serverName}.{databaseName}.{collectionName}", out var initiationInfo)) throw new InvalidOperationException($"Always call {nameof(ShouldInitiate)} before calling {nameof(ShouldInitiateIndex)}.");

        if (initiationInfo.IndexAssured) return false;

        var updated = initiationInfo with { IndexAssured = true };
        return _initiated.TryUpdate($"{serverName}.{databaseName}.{collectionName}", updated, initiationInfo);
    }

    public void AddFailedInitiateIndex(string serverName, string databaseName, string collectionName, IndexFailOperation operation, string indexName, string errorMessage)
    {
        if (!_initiated.TryGetValue($"{serverName}.{databaseName}.{collectionName}", out var initiationInfo)) throw new InvalidOperationException($"Always call {nameof(ShouldInitiate)} before calling {nameof(ShouldInitiateIndex)}.");

        // ConcurrentDictionary's indexer is upsert — idempotent on the (op, name) key,
        // overwrites the latest error message every time.
        initiationInfo.FailedIndices[(operation, indexName)] = errorMessage;
    }

    public bool RecheckInitiateIndex(string serverName, string databaseName, string collectionName)
    {
        if (!_initiated.TryGetValue($"{serverName}.{databaseName}.{collectionName}", out var initiationInfo)) return false;
        if (initiationInfo.FailedIndices.IsEmpty) return false;

        var updated = initiationInfo with { IndexAssured = false };
        _ = _initiated.TryUpdate($"{serverName}.{databaseName}.{collectionName}", updated, initiationInfo);
        return true;
    }

    public IReadOnlyList<IndexFailure> GetFailedIndices(string serverName, string databaseName, string collectionName)
    {
        if (!_initiated.TryGetValue($"{serverName}.{databaseName}.{collectionName}", out var initiationInfo)) return [];
        return initiationInfo.FailedIndices
            .Select(kvp => new IndexFailure { Operation = kvp.Key.Operation, Name = kvp.Key.Name, LastErrorMessage = kvp.Value })
            .ToArray();
    }
}

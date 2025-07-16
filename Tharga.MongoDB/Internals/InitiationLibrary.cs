using System;
using System.Collections.Concurrent;

namespace Tharga.MongoDB.Internals;

internal static class InitiationLibrary
{
    private static readonly ConcurrentDictionary<string, bool> _initiated = new();

    public static bool ShouldInitiate(string serverName, string databaseName, string collectionName)
    {
        return _initiated.TryAdd($"{serverName}.{databaseName}.{collectionName}", false);
    }

    public static bool ShouldInitiateIndex(string serverName, string databaseName, string collectionName)
    {
        if (!_initiated.TryGetValue($"{serverName}.{databaseName}.{collectionName}", out var assureIndex)) throw new InvalidOperationException($"Always call {nameof(ShouldInitiate)} before calling {nameof(ShouldInitiateIndex)}.");
        if (assureIndex) return false;
        return _initiated.TryUpdate($"{serverName}.{databaseName}.{collectionName}", true, false);
    }
}
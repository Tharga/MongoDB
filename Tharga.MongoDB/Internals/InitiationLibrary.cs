using System;
using System.Collections.Concurrent;

namespace Tharga.MongoDB.Internals;

internal static class InitiationLibrary
{
    private static readonly ConcurrentDictionary<string, bool> Initiated = new();

    public static bool ShouldInitiate(string serverName, string databaseName, string collectionName)
    {
        return Initiated.TryAdd($"{serverName}.{databaseName}.{collectionName}", false);
    }

    public static bool ShouldInitiateIndex(string serverName, string databaseName, string collectionName)
    {
        if (!Initiated.TryGetValue($"{serverName}.{databaseName}.{collectionName}", out var assureIndex)) throw new InvalidOperationException($"Always call {nameof(ShouldInitiate)} before calling {nameof(ShouldInitiateIndex)}.");
        if (assureIndex) return false;
        return Initiated.TryUpdate($"{serverName}.{databaseName}.{collectionName}", true, false);
    }
}
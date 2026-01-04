using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Tharga.MongoDB.Internals;

internal static class InitiationLibrary
{
    private static readonly ConcurrentDictionary<string, InitiationInfo> _initiated = new();

    public static bool ShouldInitiate(string serverName, string databaseName, string collectionName)
    {
        return _initiated.TryAdd($"{serverName}.{databaseName}.{collectionName}", new InitiationInfo { IndexAssured = false });
    }

    public static bool ShouldInitiateIndex(string serverName, string databaseName, string collectionName)
    {
        if (!_initiated.TryGetValue($"{serverName}.{databaseName}.{collectionName}", out var initiationInfo)) throw new InvalidOperationException($"Always call {nameof(ShouldInitiate)} before calling {nameof(ShouldInitiateIndex)}.");

        if (initiationInfo.IndexAssured) return false;

        var updated = initiationInfo with { IndexAssured = true };
        return _initiated.TryUpdate($"{serverName}.{databaseName}.{collectionName}", updated, initiationInfo);
    }

    //public static long? GetVirtualCount(string serverName, string databaseName, string collectionName)
    //{
    //    if (!_initiated.TryGetValue($"{serverName}.{databaseName}.{collectionName}", out var initiationInfo)) return null;
    //    return initiationInfo.VirtualCount;
    //}

    //public static void SetVirtualCount(string serverName, string databaseName, string collectionName, long virtualCount)
    //{
    //    if (!_initiated.TryGetValue($"{serverName}.{databaseName}.{collectionName}", out var initiationInfo)) throw new InvalidOperationException($"Always call {nameof(ShouldInitiate)} before calling {nameof(ShouldInitiateIndex)}.");
    //    initiationInfo.VirtualCount = virtualCount;
    //}

    //public static void IncreaseCount(string serverName, string databaseName, string collectionName)
    //{
    //    if (!_initiated.TryGetValue($"{serverName}.{databaseName}.{collectionName}", out var initiationInfo)) throw new InvalidOperationException($"Always call {nameof(ShouldInitiate)} before calling {nameof(ShouldInitiateIndex)}.");
    //    initiationInfo.VirtualCount++;
    //}

    public static void AddFailedInitiateIndex(string serverName, string databaseName, string collectionName, (IndexFailOperation Drop, string indexName) valueTuple)
    {
        if (!_initiated.TryGetValue($"{serverName}.{databaseName}.{collectionName}", out var initiationInfo)) throw new InvalidOperationException($"Always call {nameof(ShouldInitiate)} before calling {nameof(ShouldInitiateIndex)}.");
        initiationInfo.FailedIndices.Add(valueTuple);
    }

    public static void RecheckInitiateIndex(string serverName, string databaseName, string collectionName)
    {
        if (!_initiated.TryGetValue($"{serverName}.{databaseName}.{collectionName}", out var initiationInfo)) return;
        if (!initiationInfo.FailedIndices.Any()) return;

        var updated = initiationInfo with { IndexAssured = false };
        _ = _initiated.TryUpdate($"{serverName}.{databaseName}.{collectionName}", updated, initiationInfo);
    }

    public static IEnumerable<(IndexFailOperation Operation, string Name)> GetFailedIndices(string serverName, string databaseName, string collectionName)
    {
        if (!_initiated.TryGetValue($"{serverName}.{databaseName}.{collectionName}", out var initiationInfo)) throw new InvalidOperationException($"Always call {nameof(ShouldInitiate)} before calling {nameof(ShouldInitiateIndex)}.");
        return initiationInfo.FailedIndices;
    }
}

internal record InitiationInfo
{
    public bool IndexAssured { get; set; }
    public long? VirtualCount { get; set; }
    public List<(IndexFailOperation Operation, string Name)> FailedIndices { get; set; } = new();
}
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tharga.MongoDB;

/// <summary>
/// Operator-editable, persisted settings for one cluster (keyed by its host(s)). Drives the Queue view's
/// per-cluster connection bar (limit + thresholds) and its display name (alias). Stored centrally on the
/// monitor server and read on the render path, so reads are from an in-memory cache.
/// </summary>
public record ClusterConfigEntry
{
    /// <summary>The cluster identity — server host(s), e.g. <c>localhost:27017</c> or <c>cluster0.ab12.mongodb.net</c>. Acts as the key.</summary>
    public required string Cluster { get; init; }

    /// <summary>Friendly name shown instead of the raw host (e.g. "Production"). Null = show the host.</summary>
    public string Alias { get; init; }

    /// <summary>Atlas tier name (e.g. "M30") whose known limit is used. Null = use <see cref="Limit"/> instead.</summary>
    public string Tier { get; init; }

    /// <summary>Manual connection limit, used when <see cref="Tier"/> is null/unknown. Null = no limit (no bar).</summary>
    public int? Limit { get; init; }

    /// <summary>Override for the bar's amber point as a fraction (0..1). Null = use the global default.</summary>
    public double? WarnThreshold { get; init; }

    /// <summary>Override for the bar's red point as a fraction (0..1). Null = use the global default.</summary>
    public double? DangerThreshold { get; init; }

    /// <summary>The effective connection limit: the tier's known limit if a known tier is set, else the manual <see cref="Limit"/>, else null.</summary>
    public int? EffectiveLimit => AtlasTier.LimitFor(Tier) ?? Limit;
}

/// <summary>
/// Known MongoDB Atlas connection limits per cluster tier. Indicative values — verify against current Atlas
/// docs, as limits change over time and vary by cluster class.
/// </summary>
public static class AtlasTier
{
    public static readonly IReadOnlyDictionary<string, int> Limits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["M0"] = 500, ["M2"] = 500, ["M5"] = 500,
        ["M10"] = 1500,
        ["M20"] = 3000, ["M30"] = 3000,
        ["M40"] = 6000,
        ["M50"] = 16000,
        ["M60"] = 32000,
        ["M80"] = 96000, ["M140"] = 96000,
        ["M200"] = 128000, ["M300"] = 128000,
    };

    /// <summary>Tier names in size order, for a picker.</summary>
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "M0", "M2", "M5", "M10", "M20", "M30", "M40", "M50", "M60", "M80", "M140", "M200", "M300",
    };

    public static int? LimitFor(string tier)
        => !string.IsNullOrEmpty(tier) && Limits.TryGetValue(tier, out var limit) ? limit : null;
}

/// <summary>Helpers for reasoning about a cluster (the server host(s)).</summary>
public static class MongoDbCluster
{
    /// <summary>True when the cluster host looks like an Atlas deployment (on <c>mongodb.net</c>); false for self-hosted / unknown.</summary>
    public static bool IsAtlas(string cluster) => Internals.MongoDbClientProvider.IsAtlasCluster(cluster);
}

/// <summary>
/// Central, persisted store of per-cluster <see cref="ClusterConfigEntry"/>. Registered on the monitor server;
/// reads are cached (safe on the render path), writes upsert to the database and refresh the cache.
/// </summary>
public interface IClusterConfigStore
{
    /// <summary>All configured clusters.</summary>
    IReadOnlyList<ClusterConfigEntry> GetAll();

    /// <summary>The entry for a cluster, or null when none is configured.</summary>
    ClusterConfigEntry Get(string cluster);

    /// <summary>The effective connection limit for a cluster: tier limit if a known tier is set, else the manual limit, else null.</summary>
    int? GetEffectiveLimit(string cluster);

    /// <summary>Upsert a cluster's configuration and refresh the cache.</summary>
    Task SetAsync(ClusterConfigEntry entry);
}

using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson.Serialization;

namespace Tharga.MongoDB;

internal static class SchemaFingerprint
{
    // Bumped to v2 when the algorithm switched from raw reflection over public
    // instance properties to BsonClassMap.AutoMap. The new algorithm only counts
    // members that MongoDB.Driver actually serializes, so fingerprints from the
    // old algorithm intentionally compare unequal — collections cleaned before
    // the upgrade show "Outdated" once until the next clean.
    private const string AlgorithmPrefix = "v2:";

    /// <summary>
    /// Returns true if <paramref name="fingerprint"/> was produced by the current
    /// algorithm version. Used to invalidate stale cache entries from older builds
    /// so the monitor recomputes them on the next refresh.
    /// </summary>
    public static bool IsCurrentVersion(string fingerprint) =>
        !string.IsNullOrEmpty(fingerprint) && fingerprint.StartsWith(AlgorithmPrefix, StringComparison.Ordinal);

    public static string Generate(Type entityType)
    {
        var classMap = new BsonClassMap(entityType);
        classMap.AutoMap();
        classMap.Freeze();

        var schema = string.Join("|", classMap.AllMemberMaps
            .OrderBy(m => m.ElementName, StringComparer.Ordinal)
            .Select(m =>
            {
                var hasFlexibleGuid = m.MemberInfo.GetCustomAttribute<FlexibleGuidAttribute>() != null;
                return $"{m.ElementName}:{m.MemberType.FullName}{(hasFlexibleGuid ? ":FlexibleGuid" : "")}";
            }));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(schema));
        return AlgorithmPrefix + Convert.ToHexString(hash)[..16];
    }
}

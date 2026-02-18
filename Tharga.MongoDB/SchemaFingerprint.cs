using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Tharga.MongoDB;

internal static class SchemaFingerprint
{
    public static string Generate(Type entityType)
    {
        var properties = entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p =>
            {
                var hasFlexibleGuid = p.GetCustomAttribute<FlexibleGuidAttribute>() != null;
                return $"{p.Name}:{p.PropertyType.FullName}{(hasFlexibleGuid ? ":FlexibleGuid" : "")}";
            });

        var schema = string.Join("|", properties);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(schema));
        return Convert.ToHexString(hash)[..16];
    }
}

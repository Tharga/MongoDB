using System;

namespace Tharga.MongoDB.Internals;

internal static class StringExtensions
{
    public static string NullIfEmpty(this string item)
    {
        if (string.IsNullOrEmpty(item)) return null;
        return item;
    }

    public static bool IsNullOrEmpty(this string item)
    {
        return string.IsNullOrEmpty(item);
    }

    public static string TrimEnd(this string item, string data)
    {
        var endPosA = item.LastIndexOf(data, StringComparison.Ordinal);
        if (endPosA == -1) return item;

        var endPosB = item.Length - data.Length;
        if (endPosA != endPosB) return item;

        var result = item.Substring(0, endPosA);
        return result;
    }
}
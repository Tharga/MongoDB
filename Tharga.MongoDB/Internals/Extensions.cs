namespace Tharga.MongoDB.Internals;

internal static class Extensions
{
    public static string ProtectCollectionName(this string collectionName)
    {
        var result = collectionName.Replace("//", "-")
            .Replace(":", "")
            .Replace(" ", "_")
            .Replace(".", "_").Trim();

        if (result.Contains("/"))
        {
            var p = result.IndexOf('/');
            result = result.Substring(0, p);
        }

        return result;
    }
}
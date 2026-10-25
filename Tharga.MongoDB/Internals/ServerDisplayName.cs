using System;

namespace Tharga.MongoDB.Internals;

internal static class ServerDisplayName
{
    private const string SchemeSeparator = "://";

    public static string From(string urlOrServer)
    {
        if (string.IsNullOrEmpty(urlOrServer)) return string.Empty;

        var schemeEnd = urlOrServer.IndexOf(SchemeSeparator, StringComparison.Ordinal);
        var withoutScheme = schemeEnd < 0 ? urlOrServer : urlOrServer[(schemeEnd + SchemeSeparator.Length)..];
        var withoutQuery = withoutScheme.Split('?')[0];
        var withoutUserInfo = withoutQuery[(withoutQuery.LastIndexOf('@') + 1)..];
        return withoutUserInfo.Split('/')[0];
    }
}

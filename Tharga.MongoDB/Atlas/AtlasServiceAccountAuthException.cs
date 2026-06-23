using System;
using System.Net;

namespace Tharga.MongoDB.Atlas;

/// <summary>
/// Thrown when an Atlas Service Account OAuth2 token request is rejected (HTTP 401) — the client
/// secret is invalid or expired. Atlas returns 401 for both cases, so <see cref="LikelyExpired"/>
/// is a best-effort flag set only when the response body mentions expiry.
/// </summary>
public sealed class AtlasServiceAccountAuthException : Exception
{
    public AtlasServiceAccountAuthException(HttpStatusCode statusCode, bool likelyExpired) : base(BuildMessage(statusCode, likelyExpired))
    {
        StatusCode = statusCode;
        LikelyExpired = likelyExpired;
    }

    public HttpStatusCode StatusCode { get; }

    public bool LikelyExpired { get; }

    private static string BuildMessage(HttpStatusCode statusCode, bool likelyExpired)
    {
        if (likelyExpired) return "Atlas service account authentication failed (HTTP 401): the client secret appears to be expired — rotate the service-account secret in Atlas.";
        return $"Atlas service account authentication failed (HTTP {(int)statusCode}): the client secret may be invalid or expired.";
    }
}

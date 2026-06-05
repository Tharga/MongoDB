using System;

namespace Tharga.MongoDB.Atlas;

/// <summary>
/// Thrown when a Quilt4Net firewall proxy call is rejected with 401/403 — the API key was
/// revoked, lacks the required <c>firewall:*</c> scope, or targets a project (group) it is not
/// bound to. Distinct from transient HTTP failures so the heartbeat loop can drop the entry
/// rather than retrying a misconfigured key forever.
/// </summary>
public sealed class Quilt4NetFirewallAuthorizationException : Exception
{
    public Quilt4NetFirewallAuthorizationException(string message) : base(message) { }
}

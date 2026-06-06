# Atlas firewall management

When a Tharga.MongoDB-hosted service talks to an Atlas cluster (anything other than `localhost`), the consumer's egress IP must be on the cluster's IP access list. Tharga.MongoDB can manage that automatically — either by calling Atlas directly with an organisation API key, or by delegating to [Quilt4Net's firewall proxy](https://www.nuget.org/packages/Quilt4Net.Toolkit) so individual services don't need to hold an Atlas credential.

The mode is **inferred** from which keys you populate on `MongoDbApiAccess` — there is no explicit enum to set.

## Configuration matrix

Populate `MongoDbApiAccess` on the relevant configuration's `AccessInfo`:

```csharp
services.AddMongoDB(o =>
{
    o.ConfigurationLoader = (sp) => Task.FromResult(new MongoDbConfigurationTree
    {
        AccessInfo = new MongoDbApiAccess
        {
            // Atlas direct (Classic / Notify):
            PublicKey  = "<atlas-public-key>",
            PrivateKey = "<atlas-private-key>",

            // Required for both Atlas-direct and Quilt4Net paths — it's the Atlas project ID.
            GroupId    = "<atlas-group-id>",

            // Quilt4Net proxy (Notify / Open):
            Quilt4NetBaseUrl = "https://your-quilt4net.example.com/", // defaults to https://quilt4net.com/
            Quilt4NetApiKey  = "<quilt4net-firewall-key>",

            Name = "{machineName}-{environment}",
        },
    }));
});
```

| Atlas keys (Public+Private) | Quilt4Net key | Mode | What happens |
|:--:|:--:|:--|:--|
| ✔ | ✘ | **Classic** | Direct Atlas API call adds your egress IP to the project's access list. Default and unchanged behaviour. |
| ✔ | ✔ | **Notify** | Direct Atlas API call (same as Classic) **plus** a periodic `ReportUsedAsync` to Quilt4Net so the central system tracks that this IP is in active use. |
| ✘ | ✔ | **Open** | Tharga.MongoDB calls Quilt4Net's proxy `OpenAsync`. Quilt4Net performs the Atlas change server-side. Subsequent heartbeats reuse the same `OpenAsync` call — when the firewall is already open Quilt4Net returns `AlreadyOpen`, which doubles as the usage signal (no separate `ReportUsedAsync` round-trip). The consumer never holds an Atlas credential. |
| ✘ | ✘ | None | No firewall management. Used for localhost development or when something else manages the access list. |

## Heartbeat

In **Notify** and **Open** mode a background service periodically tells Quilt4Net the opening is still in use, so its auto-close sweeper defers removing it. Tune via `DatabaseOptions.Quilt4NetHeartbeatInterval`:

```csharp
services.AddMongoDB(o =>
{
    o.Quilt4NetHeartbeatInterval = TimeSpan.FromMinutes(5); // default
    // o.Quilt4NetHeartbeatInterval = null; // disable the heartbeat service entirely
});
```

The service is dormant when no access is in Notify/Open mode — consumers without a `Quilt4NetApiKey` pay nothing at runtime.

## Auth failures vs. transient failures

If the Quilt4Net proxy returns 401 or 403 the heartbeat service drops the entry and stops calling — that's `Quilt4NetFirewallAuthorizationException`, raised when the key has been revoked, lacks the required `firewall:*` scope, or targets a group it's not bound to. The exception is **public**, so callers triggering a direct `OpenAsync` (e.g. via `IMongoDbFirewallStateService.AssureFirewallAccessAsync`) can catch and react.

Transient HTTP errors (5xx, network blips) keep the entry in the heartbeat loop so the next tick retries.

## Building Atlas keys

For direct (Classic / Notify) mode you need an Atlas organisation API key pair:

1. Sign in to Atlas, open the *Access Manager* for the **organisation**.
2. Under the *Applications* tab choose *API Keys* and create a key pair with the *Organization Project Creator* (or stricter) role and project access for the target group.
3. The *GroupId* is in the Atlas URL: `https://cloud.mongodb.com/v2/<GroupId>`.

For Quilt4Net (Notify / Open) mode you need a firewall key for the right Atlas project. Open mode requires a `firewall:manage` scope key; Notify mode works with `firewall:usage`. The key is bound to one Atlas project, so the `GroupId` you set on `MongoDbApiAccess` must match the group the key was issued for.

## See also

- [API: `MongoDbApiAccess`](xref:Tharga.MongoDB.Configuration.MongoDbApiAccess)
- [API: `DatabaseOptions.Quilt4NetHeartbeatInterval`](xref:Tharga.MongoDB.Configuration.DatabaseOptions)
- The Atlas-side IP access list reference on [mongodb.com](https://www.mongodb.com/docs/atlas/security/ip-access-list/).

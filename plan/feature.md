# Feature: Stable schema fingerprint that survives read-only properties

## Goal

When a collection is cleaned, the **Collection** dialog should report **Current** afterwards — not **Outdated**. Today this works most of the time but fails "sometimes" in a way the user's backlog note attributes to `SchemaFingerprint.Generate` mishandling read-only properties.

## Background

`CollectionDialog.razor:129` defines outdated as `_model.Clean.SchemaFingerprint != _model.CurrentSchemaFingerprint`. Both values flow from the same function — `SchemaFingerprint.Generate(Type)` — so for the same `Type` they should always agree. The fact that they don't, "sometimes", points at one of three failure modes:

| Suspect | Mechanism |
|---|---|
| **A. Hidden inherited properties** (`new` keyword) | `GetProperties` returns base + derived occurrences of the same name. `OrderBy(p.Name)` is unstable for equal keys → hash is non-deterministic across reflection runs. |
| **B. Pure computed get-only properties** (`=> expression`, `{ get; }` without backing field) | Included in the fingerprint, but **not** serialized into BSON by `MongoDB.Driver`. The fingerprint records schema the actual document never carries. |
| **C. Stale persisted `CurrentSchemaFingerprint`** | [`DatabaseMonitor.cs:400`](../Tharga.MongoDB/DatabaseMonitor.cs) reuses `existing.CurrentSchemaFingerprint ?? Compute(...)`. Once persisted from an older build, never recomputed even when the type definition changes. |

The backlog note specifically suspects **B**. Phase 1 reproduces all three to confirm which one(s) are real before fixing.

## Scope

1. **Reproduce**: a focused test suite that exercises `SchemaFingerprint.Generate` against the property shapes above, including stability runs (generate 100 times, assert determinism).
2. **Fix the underlying algorithm**: align the fingerprint with what `MongoDB.Driver` actually serializes. Likely path: walk `BsonClassMap.AutoMap(type).AllMemberMaps` (or equivalent) instead of raw reflection — guarantees the hash describes the on-disk schema, not the C# surface.
3. **Stale-cache mitigation** (only if the algorithm changes): decide between (a) embedding an algorithm-version prefix in the fingerprint string so old vs new are visibly distinguishable, (b) forcing recompute on cache load when the version prefix doesn't match. Goal: existing deployments see "Outdated" exactly once after upgrade, until the next clean.
4. **NuGet patch bundle** (in-scope, low-risk): `Microsoft.Extensions.*` 10.0.7 → 10.0.8, `MongoDB.Driver` 3.8.0 → 3.8.1, `Tharga.Runtime` 0.1.11 → 0.1.12, `Microsoft.SourceLink.GitHub` 10.0.203 → 10.0.300, `Tharga.Test.Toolkit` 1.14.8 → 1.14.9, `FluentAssertions` 8.9.0 → 8.10.0.

## Out of scope

- `SharpCompress` 0.48.0 → 1.0.0 — major bump, the current pin is the CVE fix from `snappier-cve`. Needs a separate verification PR.
- Read-only property handling at the *serialization* layer — `MongoDB.Driver` already has well-defined behavior; we're only fixing the fingerprint side.

## Acceptance criteria

- Cleaning a collection produces `Clean.SchemaFingerprint == CurrentSchemaFingerprint`, deterministically, for entity types containing:
  - read-only auto-properties (`{ get; }`)
  - init-only properties (`{ get; init; }`)
  - computed properties (`=> expression`)
  - inherited base-class properties
  - hidden inherited properties (via `new`)
- 100-iteration stability test passes (same type → same hash, every time).
- All existing tests stay green; new tests cover each property shape.
- Documentation note added (README or release notes) explaining the one-time "Outdated" status after upgrade, if the algorithm changes.

## Done condition

- Acceptance criteria met.
- PR opened, reviewed, merged.
- Backlog entry removed from `MongoDB.md`.

## Validation environment

No multi-process setup required — unit tests are sufficient for the fingerprint algorithm itself. Optionally smoke against Eplicta if Phase 3's stale-cache mitigation needs real `_monitor` persistence verification.

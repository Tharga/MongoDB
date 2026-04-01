# Feature: Remote collection metadata in Monitor.Server

## Source
Follow-up from feature #12 (feature #14)

## Originating Branch
develop

## Goal
Show collection metadata from remote agents in the Blazor CollectionView alongside local collections, deduplicated by fingerprint key.

## Design Decisions
- Enrich `MonitorCollectionInfoMessage` with stats, index info, clean info, registration, discovery
- Client forwards collection info on `CollectionInfoChangedEvent` and on initial connect
- Server stores remote collection info in-memory keyed by fingerprint
- `GetInstancesAsync` merges local + remote; deduplicates by fingerprint key (local wins)
- Remote-only collections get `Registration.Remote` (new enum value) — UI hides action buttons for these
- `CollectionInfo.CollectionType` is null for remote entries (non-serializable `Type`)

## Scope
- Enrich `MonitorCollectionInfoMessage` with metadata fields
- Client: forward collection info via `MonitorForwarder`
- Server: handler + storage for remote collection info
- `IDatabaseMonitor`: ingest + merge remote collection data
- Blazor: hide action buttons for remote collections
- Deduplication: same fingerprint from multiple sources = one entry

## Acceptance Criteria
- [ ] Client forwards collection metadata on change
- [ ] Server stores and deduplicates remote collection info
- [ ] `GetInstancesAsync` returns merged local + remote data
- [ ] Remote collections show in CollectionView without action buttons
- [ ] Same collection from multiple sources appears once
- [ ] Tests cover ingestion, deduplication, and merging

## Done Condition
Blazor CollectionView shows collections from all connected agents alongside local collections.

# Feature: Remote collection metadata in Monitor.Server

## Source
Follow-up from feature #12 (monitor-server-package)

## Goal
Show collection metadata (document count, size, indices, clean status) from remote agents in the Blazor CollectionView alongside local collections.

## Scope
- Wire `MonitorCollectionInfoMessage` forwarding from Monitor.Client (message type already exists)
- Handler on Monitor.Server that receives and stores remote collection info
- Aggregate remote collection metadata into `IDatabaseMonitor.GetInstancesAsync`
- Deduplicate: the same collection accessible from multiple clients (or the server) should appear only once — merge metadata from all sources
- Distinguish local (actionable) vs remote (read-only) collections in the UI
- Remote collections should not expose Touch, Drop Index, Clean, Restore actions

## Acceptance Criteria
- [ ] Remote agent forwards collection metadata periodically or on change
- [ ] Server aggregates remote + local collections in GetInstancesAsync
- [ ] Blazor CollectionView shows remote collections
- [ ] Remote collections are visually distinct and read-only (no action buttons)
- [ ] Same collection from multiple sources appears only once (deduplicated by fingerprint key)
- [ ] Tests cover forwarding, ingestion, aggregation, and deduplication

## Done Condition
The Blazor CollectionView shows collections from all connected agents alongside local collections.

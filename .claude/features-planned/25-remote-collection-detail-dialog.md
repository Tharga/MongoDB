# Feature: Collection detail dialog for remote collections

## Source
UI gap — detail button hidden for remote-only collections

## Goal
Allow opening the collection detail dialog for remote collections. Show available info (stats, indexes, clean info) from the cached remote data. Enable actions (Touch, Drop Index, Restore Index, Clean) via the existing remote action delegation.

## Scope
- Show the detail button for remote collections (currently hidden via `IsLocal`)
- Detail dialog displays cached stats, indexes, and clean info from `RemoteCollectionInfoDto`
- Action buttons (Touch, Drop Index, etc.) trigger remote delegation when collection is not local
- Handle case where no agent is connected (disable actions, show message)
- Refresh data after remote action completes (agent sends updated collection info)

## Acceptance Criteria
- [ ] Detail dialog opens for remote collections
- [ ] Stats, indexes, and clean info are displayed from cached data
- [ ] Actions delegate to the connected agent
- [ ] Actions disabled with message when no agent is connected
- [ ] Data refreshes after action completes

## Done Condition
Users can inspect and manage remote collections from the dashboard with the same UI as local collections.

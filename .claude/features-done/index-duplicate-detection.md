# Feature: Fix duplicate detection via specific index definition

## Originating Branch
develop

## Goal
Fix the bug where `GetIndexBlockers` fails to find duplicates when the index is defined without an explicit `Name` in `CreateIndexOptions`.

## Root Cause
`GetIndexBlockers` matches by `Options.Name == indexName`, but when no name is set in code, `Options.Name` is null. MongoDB auto-generates a name (e.g. `Name_1`), so the lookup never matches.

## Fix
Fall back to matching by rendered key fields when no match by name is found. Render the keys of each code-defined index and compare against the keys of the MongoDB index with the given name.

## Acceptance Criteria
- [ ] `GetIndexBlockers` works for indices with explicit names
- [ ] `GetIndexBlockers` works for indices without explicit names (matched by key fields)
- [ ] Tests cover both scenarios
- [ ] Existing tests pass

## Done Condition
Duplicate detection works regardless of whether the index has an explicit name.

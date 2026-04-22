# Feature: Refactor `LockForUpdate` / `LockForDelete` as wrappers over the generalised lock

## Source
Requested from the Berakna project (consumer) in April 2026. Once the generalised document lock (feature #30) is in place, the existing `LockForUpdate` and `LockForDelete` APIs represent two specific pre-committed variants of the generalised lock. Keeping two separate implementations invites drift.

Depends on feature #30.

## Goal
Reimplement the existing `LockForUpdate` and `LockForDelete` APIs as thin wrappers over the generalised `LockAsync` primitive. No behaviour change for existing callers.

## Scope

- `LockForUpdate` becomes a wrapper that acquires a generalised lock and pre-sets the commit decision to `MarkForUpdate`.
- `LockForDelete` becomes a wrapper that acquires a generalised lock and pre-sets the commit decision to `MarkForDelete`.
- Existing call-sites in consumer code continue to work unchanged.
- Tests for the original APIs still pass without modification.

## Out of scope
- Changing the public surface of `LockForUpdate` / `LockForDelete`.
- Deprecating the existing APIs — they remain as ergonomic shortcuts for the common single-outcome case.

## Acceptance Criteria
- [ ] `LockForUpdate` internally delegates to the generalised `LockAsync`
- [ ] `LockForDelete` internally delegates to the generalised `LockAsync`
- [ ] Existing tests for `LockForUpdate` / `LockForDelete` pass unchanged
- [ ] No duplication of lock-acquisition or lock-release logic between the two APIs and the generalised implementation
- [ ] README continues to document both shortcuts (no changes needed unless examples benefit)

## Done Condition
The library has a single lock implementation; `LockForUpdate` and `LockForDelete` are ergonomic wrappers with no independent logic. All existing consumer code continues to work without modification.

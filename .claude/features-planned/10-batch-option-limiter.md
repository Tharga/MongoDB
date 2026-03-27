# Feature: Per-call batch option limiter on GetAsync

## Source
Backlog (Medium priority)

## Goal
Allow setting the batch option limiter per individual GetAsync call rather than only at the global/collection level.

## Scope
- Add optional batch limiter parameter to GetAsync
- Fall back to collection-level setting when not specified
- Add tests for per-call override

## Acceptance Criteria
- [ ] GetAsync accepts an optional batch limiter parameter
- [ ] Per-call value overrides collection-level default
- [ ] Omitted parameter falls back to existing behavior
- [ ] Tests cover both override and fallback

## Done Condition
Consumers can control batch limiting on a per-call basis for GetAsync.

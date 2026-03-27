# Feature: Fix Access Count for dynamic collections

## Source
Backlog (Medium priority)

## Goal
Fix the bug where Access Count shows 2 instead of 1 for dynamic collections.

## Scope
- Investigate why dynamic collections report double access count
- Fix the root cause
- Add tests for dynamic collection access counting

## Acceptance Criteria
- [ ] Dynamic collections report correct access count (1, not 2)
- [ ] Tests verify correct count for dynamic collections
- [ ] Non-dynamic collections are unaffected

## Done Condition
Access Count accurately reflects the number of accesses for dynamic collections.

# Feature: Fix duplicate detection via specific index definition

## Source
Backlog (High priority)

## Goal
Fix the bug where finding duplicates via a specific index definition does not work correctly.

## Scope
- Investigate current index duplicate detection logic
- Identify and fix the root cause
- Add tests for duplicate detection via index definition

## Acceptance Criteria
- [ ] Duplicate detection via specific index definition works correctly
- [ ] Tests cover the fixed scenario
- [ ] Existing index tests still pass

## Done Condition
Duplicates are correctly identified when using a specific index definition.

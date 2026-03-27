# Feature: Override AssureIndex per collection

## Source
Backlog (High priority)

## Goal
Allow consumers to override AssureIndex behavior per collection, similar to how `CreateCollectionStrategy` can be overridden.

## Scope
- Design an override mechanism for AssureIndex per collection
- Implement the override (e.g. via options, attribute, or virtual method)
- Add tests for custom AssureIndex behavior

## Acceptance Criteria
- [ ] Consumers can override AssureIndex behavior per collection
- [ ] Default behavior is unchanged for existing consumers
- [ ] Tests cover custom override scenarios
- [ ] Pattern is consistent with existing `CreateCollectionStrategy`

## Done Condition
Per-collection AssureIndex override is available and documented.
